#!/usr/bin/env bash
#
# Verifies the foundation stack from outside itself.
#
# Every assertion is named for the silent failure it catches, and they run cheapest first, in the
# same style as the deployed smoke test (#45). Nothing here reads Terraform state: the point is to
# check the world rather than the plan, which is also why delegation is confirmed by asking the
# `.dev` registry directly rather than by reading the console.
#
# Needs: curl, dig, jq, and an AWS session that can read Route 53, ACM and CloudFront.
#
# Usage: scripts/verify-foundation.sh [domain]
set -euo pipefail

DOMAIN="${1:-financial-compass.dev}"
ORIGIN_HOSTNAME="api-origin.${DOMAIN}"
RESOLVER="${RESOLVER:-8.8.8.8}"

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

failures=0
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n         %s\n' "$1" "${2:-}"; failures=$((failures + 1)); }
step() { printf '\n%s\n' "$1"; }

for cmd in curl dig jq aws; do
  command -v "$cmd" >/dev/null || { echo "missing required command: $cmd" >&2; exit 2; }
done

# Every AWS read goes through here. A call that fails — most often because the session lacks the
# permission — has to become a named failed assertion rather than aborting the run under `set -e`,
# or the script stops halfway and reads as "the infrastructure is broken".
aws_read() {
  local value
  value="$(aws "$@" 2>/dev/null)" || return 1
  [[ -n "$value" && "$value" != "None" ]] || return 1
  printf '%s' "$value"
}

# 1 — Delegation. If the registry still points at other name servers, everything below can be
#     correct in the account and invisible on the internet.
step "1. The .dev registry delegates ${DOMAIN} to this hosted zone (catches: the domain resolves to nobody)"
zone_id=""
if ! zone_id="$(aws_read route53 list-hosted-zones-by-name --dns-name "${DOMAIN}." \
  --query "HostedZones[?Name=='${DOMAIN}.'].Id | [0]" --output text)"; then
  fail "a public hosted zone exists for ${DOMAIN}" "list-hosted-zones-by-name found none, or the session cannot read Route 53"
else
  zone_id="${zone_id#/hostedzone/}"
  pass "hosted zone ${zone_id} exists for ${DOMAIN}"

  if ! zone_ns="$(aws_read route53 get-hosted-zone --id "$zone_id" \
    --query 'DelegationSet.NameServers' --output text)"; then
    fail "the hosted zone reports a delegation set" "get-hosted-zone failed"
  else
    zone_ns="$(tr '\t' '\n' <<<"$zone_ns" | tr 'A-Z' 'a-z' | sed 's/\.$//' | sort -u)"

    # Asked of the registry itself, not of a recursive resolver's cached answer and not of the
    # zone's own apex NS record — those are the zone talking about itself.
    tld="${DOMAIN#*.}"
    registry_ns="$(dig +short NS "${tld}." "@${RESOLVER}" | sed -n '1p' || true)"
    if [[ -z "$registry_ns" ]]; then
      fail "the ${tld} registry answers" "no NS records for ${tld} via ${RESOLVER}"
    else
      delegated_ns="$(dig +norecurse +noall +authority NS "${DOMAIN}." "@${registry_ns}" \
        | awk '$4 == "NS" { print tolower($5) }' | sed 's/\.$//' | sort -u || true)"
      if [[ -z "$delegated_ns" ]]; then
        fail "${tld} delegates ${DOMAIN}" "${registry_ns} returned no delegation records"
      elif [[ "$zone_ns" == "$delegated_ns" ]]; then
        pass "${registry_ns} delegates ${DOMAIN} to exactly this zone's name servers"
      else
        fail "${registry_ns} delegates ${DOMAIN} to exactly this zone's name servers" \
          "zone: $(tr '\n' ' ' <<<"$zone_ns")/ registry: $(tr '\n' ' ' <<<"$delegated_ns")"
      fi
    fi
  fi
fi

# 2 — Certificates. The classic first-attempt failure is requesting CloudFront's certificate in
#     the stack's own region, where CloudFront cannot see it.
step "2. Both certificates are issued, each in the region that consumes it (catches: the us-east-1 rule)"
if aws_read acm list-certificates --region us-east-1 --certificate-statuses ISSUED \
  --query "CertificateSummaryList[?DomainName=='${DOMAIN}'].CertificateArn | [0]" --output text >/dev/null; then
  pass "CloudFront's certificate for ${DOMAIN} is ISSUED in us-east-1"
else
  fail "CloudFront's certificate for ${DOMAIN} is ISSUED in us-east-1" \
    "CloudFront reads certificates only from N. Virginia, whatever region the stack runs in"
fi
if aws_read acm list-certificates --region ap-southeast-2 --certificate-statuses ISSUED \
  --query "CertificateSummaryList[?DomainName=='${ORIGIN_HOSTNAME}'].CertificateArn | [0]" --output text >/dev/null; then
  pass "the origin certificate for ${ORIGIN_HOSTNAME} is ISSUED in ap-southeast-2"
else
  fail "the origin certificate for ${ORIGIN_HOSTNAME} is ISSUED in ap-southeast-2" \
    "the ALB's certificate must live in the ALB's own region"
fi

# 3 — The apex record. A CNAME at an apex is illegal, so getting this wrong means the name does
#     not resolve at all. DNS alone cannot tell an alias from a plain A, so ask Route 53.
step "3. The apex resolves through alias records (catches: an illegal apex CNAME)"
if [[ -n "$zone_id" ]]; then
  if ! apex="$(aws_read route53 list-resource-record-sets --hosted-zone-id "$zone_id" \
    --query "ResourceRecordSets[?Name=='${DOMAIN}.']" --output json)"; then
    fail "the apex's records can be read" "list-resource-record-sets failed"
  else
    for type in A AAAA; do
      if [[ "$(jq -r --arg t "$type" '[.[] | select(.Type == $t and .AliasTarget != null)] | length' <<<"$apex")" -ge 1 ]]; then
        pass "the apex has an alias ${type} record"
      else
        fail "the apex has an alias ${type} record" "found: $(jq -r '[.[].Type] | join(", ")' <<<"$apex")"
      fi
    done
    if [[ "$(jq -r '[.[] | select(.Type == "CNAME")] | length' <<<"$apex")" -eq 0 ]]; then
      pass "the apex has no CNAME"
    else
      fail "the apex has no CNAME" "an apex CNAME is illegal and will not resolve"
    fi
  fi
fi

# 4 — The front door itself.
step "4. The SPA loads at the root over HTTPS (catches: a broken bucket, origin access or certificate)"
root="$(curl -sS --max-time 20 -o "${workdir}/root" -w '%{http_code} %{content_type}' "https://${DOMAIN}/" || echo "000 -")"
if [[ "${root%% *}" == "200" && "$root" == *"text/html"* ]]; then
  pass "GET https://${DOMAIN}/ returned 200 text/html"
else
  fail "GET https://${DOMAIN}/ returned 200 text/html" "got: ${root}"
fi
if grep -q 'id="root"' "${workdir}/root" 2>/dev/null; then
  pass "the response body is the built SPA"
else
  fail "the response body is the built SPA" "no #root element in the response"
fi

# 5 — Deep links. The SPA owns /login and /register client-side; without the router function they
#     are 404s from the bucket, and the app looks broken to anyone who bookmarks a page.
step "5. A client-side route also serves the SPA (catches: deep links 404ing from the bucket)"
for path in /login /login/; do
  deep="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' "https://${DOMAIN}${path}" || echo 000)"
  if [[ "$deep" == "200" ]]; then
    pass "GET ${path} returned 200"
  else
    fail "GET ${path} returned 200" "got HTTP ${deep}"
  fi
done

# 6 — The decoupling. Until the app stack exists, /api/* must fail at the ORIGIN. An HTML 200 here
#     means CloudFront is serving /api/* from the SPA bucket, which would hide the API entirely.
step "6. /api/* reaches the origin hostname and fails there (catches: /api/* falling through to the SPA)"
api="$(curl -sS --max-time 30 -o /dev/null -w '%{http_code} %{content_type}' "https://${DOMAIN}/api/health" || echo "000 -")"
case "${api%% *}" in
  200)
    fail "GET /api/health returns an origin error until the app stack exists" \
      "got 200 (${api#* }) — /api/* is being served by the SPA origin, not routed to ${ORIGIN_HOSTNAME}" ;;
  502 | 503 | 504)
    pass "GET /api/health returned ${api%% *} — the origin hostname does not answer yet" ;;
  *)
    fail "GET /api/health returns an origin error until the app stack exists" "got HTTP ${api%% *}" ;;
esac

# 7 — The cookie rule, asserted while nothing answers. CloudFront strips cookies by default: get
#     this wrong and the SPA renders perfectly while every authenticated call 401s.
step "7. /api/* is configured with caching disabled and Cookie forwarded (catches: the silent auth failure)"
if ! dist="$(aws_read cloudfront list-distributions \
  --query "DistributionList.Items[?contains(Aliases.Items || \`[]\`, '${DOMAIN}')] | [0].Id" --output text)"; then
  fail "a CloudFront distribution is aliased to ${DOMAIN}" "none found, or the session cannot read CloudFront"
elif ! cfg="$(aws_read cloudfront get-distribution-config --id "$dist" --output json)"; then
  fail "the distribution's configuration can be read" "get-distribution-config failed for ${dist}"
else
  pass "distribution ${dist} is aliased to ${DOMAIN}"
  behaviour="$(jq -r '.DistributionConfig.CacheBehaviors.Items[]? | select(.PathPattern == "/api/*")' <<<"$cfg")"
  if [[ -z "$behaviour" ]]; then
    fail "the distribution has an /api/* cache behaviour" "no behaviour matches /api/*"
  else
    origin="$(jq -r --arg id "$(jq -r '.TargetOriginId' <<<"$behaviour")" \
      '.DistributionConfig.Origins.Items[] | select(.Id == $id)' <<<"$cfg")"

    if [[ "$(jq -r '.DomainName' <<<"$origin")" == "$ORIGIN_HOSTNAME" ]]; then
      pass "/api/* targets the origin hostname ${ORIGIN_HOSTNAME}, not a resource reference"
    else
      fail "/api/* targets the origin hostname ${ORIGIN_HOSTNAME}" "it targets $(jq -r '.DomainName' <<<"$origin")"
    fi

    protocol="$(jq -r '.CustomOriginConfig.OriginProtocolPolicy' <<<"$origin")"
    if [[ "$protocol" == "https-only" ]]; then
      pass "CloudFront reaches the origin over HTTPS only"
    else
      fail "CloudFront reaches the origin over HTTPS only" \
        "OriginProtocolPolicy is ${protocol} — plain HTTP would put the session cookie on the wire"
    fi

    if cache_policy="$(aws_read cloudfront get-cache-policy --id "$(jq -r '.CachePolicyId' <<<"$behaviour")" \
      --query 'CachePolicy.CachePolicyConfig.Name' --output text)" \
      && [[ "$cache_policy" == "Managed-CachingDisabled" ]]; then
      pass "/api/* uses ${cache_policy} — authenticated responses are never cached"
    else
      fail "/api/* disables caching" "cache policy is ${cache_policy:-unreadable}"
    fi

    policy_id="$(jq -r '.OriginRequestPolicyId // empty' <<<"$behaviour")"
    if [[ -z "$policy_id" ]]; then
      fail "/api/* forwards Cookie to the origin" \
        "no origin request policy — CloudFront strips cookies by default and auth fails silently"
    elif ! policy="$(aws_read cloudfront get-origin-request-policy --id "$policy_id" --output json)"; then
      fail "/api/* forwards Cookie to the origin" "origin request policy ${policy_id} could not be read"
    else
      cookies="$(jq -r '.OriginRequestPolicy.OriginRequestPolicyConfig.CookiesConfig.CookieBehavior' <<<"$policy")"
      name="$(jq -r '.OriginRequestPolicy.OriginRequestPolicyConfig.Name' <<<"$policy")"
      if [[ "$cookies" == "all" || "$cookies" == "allExcept" ]]; then
        pass "/api/* forwards cookies to the origin (${name}, CookieBehavior=${cookies})"
      else
        fail "/api/* forwards Cookie to the origin" "${name} has CookieBehavior=${cookies}"
      fi
    fi
  fi
fi

printf '\n'
if [[ "$failures" -eq 0 ]]; then
  printf '\033[32mThe foundation stack is up and the front door is proven.\033[0m\n'
  exit 0
fi
printf '\033[31m%d assertion(s) failed.\033[0m\n' "$failures"
exit 1
