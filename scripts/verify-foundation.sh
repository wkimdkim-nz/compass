#!/usr/bin/env bash
#
# Verifies the foundation stack from outside itself.
#
# Every assertion is named for the silent failure it catches, and they run cheapest first, in the
# same style as the deployed smoke test (#45). Nothing here reads Terraform state: the point is to
# check the world rather than the plan, which is also why delegation is confirmed with `dig`
# against a public resolver rather than read off the console.
#
# Needs: curl, dig, jq, and an AWS session that can read Route 53, ACM and CloudFront.
#
# Usage: scripts/verify-foundation.sh [domain]
set -Eeuo pipefail

DOMAIN="${1:-financial-compass.dev}"
ORIGIN_HOSTNAME="api-origin.${DOMAIN}"
RESOLVER="${RESOLVER:-8.8.8.8}"

failures=0
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n         %s\n' "$1" "${2:-}"; failures=$((failures + 1)); }
step() { printf '\n%s\n' "$1"; }

require() {
  for cmd in "$@"; do
    command -v "$cmd" >/dev/null || { echo "missing required command: $cmd" >&2; exit 2; }
  done
}
require curl dig jq aws

# 1 — Delegation. If the registrar still points at some other name servers, everything below can
#     be correct in the account and invisible on the internet.
step "1. Delegation reaches the hosted zone (catches: the domain resolves to nobody)"
zone_id="$(aws route53 list-hosted-zones-by-name --dns-name "${DOMAIN}." \
  --query "HostedZones[?Name=='${DOMAIN}.'].Id | [0]" --output text 2>/dev/null || true)"
if [[ -z "$zone_id" || "$zone_id" == "None" ]]; then
  fail "a public hosted zone exists for ${DOMAIN}" "list-hosted-zones-by-name found none"
else
  zone_id="${zone_id#/hostedzone/}"
  pass "hosted zone ${zone_id} exists for ${DOMAIN}"

  zone_ns="$(aws route53 get-hosted-zone --id "$zone_id" \
    --query 'DelegationSet.NameServers' --output text | tr '\t' '\n' | sed 's/\.$//' | sort)"
  public_ns="$(dig +short NS "${DOMAIN}" "@${RESOLVER}" | sed 's/\.$//' | sort)"
  if [[ -z "$public_ns" ]]; then
    fail "${DOMAIN} is delegated publicly" "no NS records resolved via ${RESOLVER}"
  elif [[ "$zone_ns" == "$public_ns" ]]; then
    pass "the public NS set matches the hosted zone's delegation set"
  else
    fail "the public NS set matches the hosted zone's delegation set" \
      "zone: $(echo "$zone_ns" | tr '\n' ' ')/ public: $(echo "$public_ns" | tr '\n' ' ')"
  fi
fi

# 2 — Certificates. The classic first-attempt failure is requesting CloudFront's certificate in the
#     stack's own region, where CloudFront cannot see it.
step "2. Both certificates are issued, each in the region that consumes it (catches: the us-east-1 rule)"
cert_in_region() { # domain, region
  aws acm list-certificates --region "$2" --certificate-statuses ISSUED \
    --query "CertificateSummaryList[?DomainName=='$1'].CertificateArn | [0]" --output text 2>/dev/null || true
}
cf_cert="$(cert_in_region "$DOMAIN" us-east-1)"
if [[ -n "$cf_cert" && "$cf_cert" != "None" ]]; then
  pass "CloudFront's certificate for ${DOMAIN} is ISSUED in us-east-1"
else
  fail "CloudFront's certificate for ${DOMAIN} is ISSUED in us-east-1" \
    "CloudFront reads certificates only from N. Virginia, whatever region the stack runs in"
fi
alb_cert="$(cert_in_region "$ORIGIN_HOSTNAME" ap-southeast-2)"
if [[ -n "$alb_cert" && "$alb_cert" != "None" ]]; then
  pass "the origin certificate for ${ORIGIN_HOSTNAME} is ISSUED in ap-southeast-2"
else
  fail "the origin certificate for ${ORIGIN_HOSTNAME} is ISSUED in ap-southeast-2" \
    "the ALB's certificate must live in the ALB's own region"
fi

# 3 — The apex record. A CNAME at an apex is illegal, so getting this wrong means the name does not
#     resolve at all; DNS alone cannot tell an alias from a plain A, so ask Route 53.
step "3. The apex resolves through alias records (catches: an illegal apex CNAME)"
if [[ -n "${zone_id:-}" && "$zone_id" != "None" ]]; then
  apex="$(aws route53 list-resource-record-sets --hosted-zone-id "$zone_id" \
    --query "ResourceRecordSets[?Name=='${DOMAIN}.']" --output json)"
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

# 4 — The front door itself.
step "4. The SPA loads at the root over HTTPS (catches: a broken bucket, origin access or certificate)"
root="$(curl -sS --max-time 20 -o /tmp/compass-root.$$ -w '%{http_code} %{content_type}' "https://${DOMAIN}/" || echo "000 -")"
root_code="${root%% *}"
if [[ "$root_code" == "200" && "$root" == *"text/html"* ]]; then
  pass "GET https://${DOMAIN}/ returned 200 text/html"
else
  fail "GET https://${DOMAIN}/ returned 200 text/html" "got: ${root}"
fi
if grep -q 'id="root"' /tmp/compass-root.$$ 2>/dev/null; then
  pass "the response body is the built SPA, not a bucket listing"
else
  fail "the response body is the built SPA, not a bucket listing" "no #root element in the response"
fi
rm -f /tmp/compass-root.$$

# 5 — Deep links. The SPA owns /login and /register client-side; without the router function they
#     are 404s from S3, and the SPA looks broken to anyone who bookmarks a page.
step "5. A client-side route also serves the SPA (catches: deep links 404ing from the bucket)"
deep="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' "https://${DOMAIN}/login" || echo 000)"
if [[ "$deep" == "200" ]]; then
  pass "GET /login returned 200"
else
  fail "GET /login returned 200" "got HTTP ${deep}"
fi

# 6 — The decoupling. Until the ephemeral stack exists, /api/* must fail at the ORIGIN. An HTML
#     200 here means CloudFront is serving /api/* from the SPA bucket, which would hide the API.
step "6. /api/* reaches the origin hostname and fails there (catches: /api/* falling through to the SPA)"
api="$(curl -sS --max-time 30 -o /tmp/compass-api.$$ -w '%{http_code} %{content_type}' "https://${DOMAIN}/api/health" || echo "000 -")"
api_code="${api%% *}"
case "$api_code" in
  502|503|504)
    pass "GET /api/health returned ${api_code} from CloudFront — the origin hostname does not answer yet" ;;
  200)
    fail "GET /api/health returns an origin error until the ephemeral stack exists" \
      "got 200 (${api#* }) — /api/* is being served by the SPA origin, not routed to ${ORIGIN_HOSTNAME}" ;;
  *)
    fail "GET /api/health returns an origin error until the ephemeral stack exists" "got HTTP ${api_code}" ;;
esac
rm -f /tmp/compass-api.$$

# 7 — The cookie rule, asserted while nothing answers. CloudFront strips cookies by default: get
#     this wrong and the SPA renders perfectly while every authenticated call 401s.
step "7. /api/* is configured with caching disabled and Cookie forwarded (catches: the silent auth failure)"
dist="$(aws cloudfront list-distributions \
  --query "DistributionList.Items[?contains(Aliases.Items || \`[]\`, '${DOMAIN}')] | [0].Id" --output text 2>/dev/null || true)"
if [[ -z "$dist" || "$dist" == "None" ]]; then
  fail "a CloudFront distribution is aliased to ${DOMAIN}" "none found"
else
  pass "distribution ${dist} is aliased to ${DOMAIN}"
  cfg="$(aws cloudfront get-distribution-config --id "$dist" --output json)"
  api_behaviour="$(jq -r '.DistributionConfig.CacheBehaviors.Items[]? | select(.PathPattern == "/api/*")' <<<"$cfg")"
  if [[ -z "$api_behaviour" ]]; then
    fail "the distribution has an /api/* cache behaviour" "no behaviour matches /api/*"
  else
    origin_id="$(jq -r '.TargetOriginId' <<<"$api_behaviour")"
    origin_domain="$(jq -r --arg id "$origin_id" \
      '.DistributionConfig.Origins.Items[] | select(.Id == $id) | .DomainName' <<<"$cfg")"
    if [[ "$origin_domain" == "$ORIGIN_HOSTNAME" ]]; then
      pass "/api/* targets the origin hostname ${ORIGIN_HOSTNAME}, not a resource reference"
    else
      fail "/api/* targets the origin hostname ${ORIGIN_HOSTNAME}" "it targets ${origin_domain}"
    fi

    origin_protocol="$(jq -r --arg id "$origin_id" \
      '.DistributionConfig.Origins.Items[] | select(.Id == $id) | .CustomOriginConfig.OriginProtocolPolicy' <<<"$cfg")"
    if [[ "$origin_protocol" == "https-only" ]]; then
      pass "CloudFront reaches the origin over HTTPS only"
    else
      fail "CloudFront reaches the origin over HTTPS only" \
        "OriginProtocolPolicy is ${origin_protocol} — plain HTTP would put the session cookie on the wire"
    fi

    cache_policy="$(aws cloudfront get-cache-policy --id "$(jq -r '.CachePolicyId' <<<"$api_behaviour")" \
      --query 'CachePolicy.CachePolicyConfig.Name' --output text 2>/dev/null || echo "?")"
    if [[ "$cache_policy" == "Managed-CachingDisabled" ]]; then
      pass "/api/* uses ${cache_policy} — authenticated responses are never cached"
    else
      fail "/api/* disables caching" "cache policy is ${cache_policy}"
    fi

    orp_id="$(jq -r '.OriginRequestPolicyId // empty' <<<"$api_behaviour")"
    if [[ -z "$orp_id" ]]; then
      fail "/api/* forwards Cookie to the origin" \
        "no origin request policy — CloudFront strips cookies by default and auth fails silently"
    else
      orp="$(aws cloudfront get-origin-request-policy --id "$orp_id" --output json)"
      cookies="$(jq -r '.OriginRequestPolicy.OriginRequestPolicyConfig.CookiesConfig.CookieBehavior' <<<"$orp")"
      orp_name="$(jq -r '.OriginRequestPolicy.OriginRequestPolicyConfig.Name' <<<"$orp")"
      if [[ "$cookies" == "all" || "$cookies" == "allExcept" ]]; then
        pass "/api/* forwards cookies to the origin (${orp_name}, CookieBehavior=${cookies})"
      else
        fail "/api/* forwards Cookie to the origin" "${orp_name} has CookieBehavior=${cookies}"
      fi
    fi
  fi
fi

printf '\n'
if (( failures == 0 )); then
  printf '\033[32mThe foundation stack is up and the front door is proven.\033[0m\n'
else
  printf '\033[31m%d assertion(s) failed.\033[0m\n' "$failures"
fi
exit $(( failures > 0 ))
