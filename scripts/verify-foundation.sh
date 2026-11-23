#!/usr/bin/env bash
#
# Verifies the foundation stack from outside itself.
#
# Every assertion is named for the silent failure it catches, and they run cheapest first, in the
# same style as `scripts/smoke-deployed.sh`. Nothing here reads Terraform state: the point is to
# check the world rather than the plan, which is also why delegation is confirmed by asking the
# `.dev` registry directly rather than by reading the console.
#
# Needs: curl, dig, jq, and an AWS session that can read Route 53, ACM, CloudFront, EC2, RDS, ECR
# and Secrets Manager. Every one of those is a *read*; the script creates and changes nothing.
#
# Usage: scripts/verify-foundation.sh [domain]
set -euo pipefail

DOMAIN="${1:-financial-compass.dev}"
ORIGIN_HOSTNAME="api-origin.${DOMAIN}"
RESOLVER="${RESOLVER:-8.8.8.8}"
REGION="${REGION:-ap-southeast-2}"

# What `scripts/push-image.sh` builds, and what the app stack's task definition must therefore
# declare as its `cpu_architecture`.
EXPECTED_ARCH="${EXPECTED_ARCH:-arm64}"

ECR_REPOSITORY="compass-api"
ORIGIN_SECRET_NAME="compass/cloudfront-origin-header"
FINNHUB_SECRET_NAME="compass/finnhub-api-key"
# What infra/foundation/finnhub-secret.tf creates the secret holding, until the operator sets it.
FINNHUB_SECRET_PLACEHOLDER="placeholder-set-by-hand-after-apply"
CONTAINER_PORT=8080
DATABASE_PORT=5432

workdir="$(mktemp -d)"
session_note="${workdir}/session-unusable"

# The last word on an unusable session, for the paths that reach the end without passing through
# `fail` — and the guarantee that such a run never exits 0 or 1, which would read as a verdict.
on_exit() {
  local status=$?
  if [[ -s "$session_note" ]]; then
    report_unusable_session
    status=2
  fi
  rm -rf "$workdir"
  exit "$status"
}
trap on_exit EXIT

# One-shot: `fail` aborts through here, and the exit trap then finds nothing left to say.
report_unusable_session() {
  printf '\n\033[31mThe AWS session is not usable:\033[0m %s\n' "$(cat "$session_note")" >&2
  printf 'Re-authenticate and run again — the assertions below it would all fail as absent.\n' >&2
  rm -f "$session_note"
}

failures=0
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
step() { printf '\n%s\n' "$1"; }

# Always the parent shell, and always the first thing a failed read reaches — which is what makes
# it the place an unusable session becomes an abort rather than the first of forty false failures.
fail() {
  if [[ -s "$session_note" ]]; then
    report_unusable_session
    exit 2
  fi
  printf '  \033[31mFAIL\033[0m  %s\n         %s\n' "$1" "${2:-}"
  failures=$((failures + 1))
}

for cmd in curl dig jq aws; do
  command -v "$cmd" >/dev/null || { echo "missing required command: $cmd" >&2; exit 2; }
done

# Every AWS read goes through here. A call that fails — most often because the session lacks the
# permission — has to become a named failed assertion rather than aborting the run under `set -e`,
# or the script stops halfway and reads as "the infrastructure is broken".
#
# ⚠️ An **expired session** is the exception, and it has to be, because it is indistinguishable
# from absence otherwise: every remaining assertion fails at once and the report reads as an
# infrastructure that has vanished. It is a precondition of the run rather than a fact about the
# world, so it aborts with the same exit code as a missing command.
#
# It cannot abort from here, though. Nearly every caller captures the value through `$(...)`, so
# this function is running in a subshell, where `exit` sets the substitution's status and nothing
# more — the caller reads it as an ordinary failed read and carries on, which is the exact failure
# this branch exists to prevent. So it leaves the reason in a file and returns like any other
# failure; `fail` and the exit trap, both of which do run in the parent, act on it. Any other
# failure becomes a named failure.
aws_read() {
  local value stderr
  stderr="$(mktemp)"
  if ! value="$(aws "$@" 2>"$stderr")"; then
    if grep -qiE 'expired|invalid.*(token|grant)|credential' "$stderr"; then
      tr -d '\n' <"$stderr" | cut -c1-200 >"$session_note"
    fi
    rm -f "$stderr"
    return 1
  fi
  rm -f "$stderr"
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
if aws_read acm list-certificates --region "$REGION" --certificate-statuses ISSUED \
  --query "CertificateSummaryList[?DomainName=='${ORIGIN_HOSTNAME}'].CertificateArn | [0]" --output text >/dev/null; then
  pass "the origin certificate for ${ORIGIN_HOSTNAME} is ISSUED in ${REGION}"
else
  fail "the origin certificate for ${ORIGIN_HOSTNAME} is ISSUED in ${REGION}" \
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
      # Only `all` is accepted. An `allExcept` policy could exclude the session cookie and a
      # whitelist could omit it, and this assertion exists to have no silent gap of its own; the
      # stack pins the managed all-viewer policy, so `all` is also what a correct deploy reports.
      if [[ "$cookies" == "all" ]]; then
        pass "/api/* forwards all cookies to the origin (${name})"
      else
        fail "/api/* forwards Cookie to the origin" \
          "${name} has CookieBehavior=${cookies} — only 'all' is guaranteed to carry the session cookie"
      fi
    fi
  fi
fi

# 8 — The network the app stack starts in. A Fargate task with no route to an internet gateway
#     does not start slowly, it does not start at all — ECS has no control-plane exemption — and
#     the database's isolation has to be a fact about routing, not only about a security group.
step "8. The VPC gives the task a way out and the database none (catches: a task that cannot start, or a reachable database)"
if ! vpc_id="$(aws_read ec2 describe-vpcs --region "$REGION" \
  --filters "Name=tag:Name,Values=compass" --query 'Vpcs[0].VpcId' --output text)"; then
  fail "a VPC tagged compass exists" "describe-vpcs found none, or the session cannot read EC2"
else
  pass "VPC ${vpc_id} exists"

  # `field` picks what to project; the tag prefix is what says which tier a subnet is in.
  tier() {
    jq -r --arg prefix "compass-$1-" --arg field "$2" \
      '[.Subnets[] | select((.Tags[]? | select(.Key == "Name") | .Value) | startswith($prefix)) | .[$field]] | unique | .[]' \
      <<<"$subnets"
  }

  if ! subnets="$(aws_read ec2 describe-subnets --region "$REGION" \
    --filters "Name=vpc-id,Values=${vpc_id}" --output json)"; then
    fail "the VPC's subnets can be read" "describe-subnets failed"
  # The route tables decide what "public" and "private" actually mean — the tier names are only
  # tags — so a run that cannot read them must say so rather than report an isolated database it
  # never checked. Fetched in one call and resolved in jq the way AWS resolves them: a subnet with
  # no association of its own routes through the VPC's main table, so that is the table it is
  # judged by, rather than being counted as a subnet with no routes at all.
  elif ! route_tables="$(aws_read ec2 describe-route-tables --region "$REGION" \
    --filters "Name=vpc-id,Values=${vpc_id}" --output json)"; then
    fail "the VPC's route tables can be read" "describe-route-tables failed"
  else
    for name in public private; do
      azs="$(tier "$name" AvailabilityZone | tr '\n' ' ')"
      if [[ "$(wc -w <<<"$azs")" -ge 2 ]]; then
        pass "the ${name} tier spans two availability zones (${azs% })"
      else
        fail "the ${name} tier spans two availability zones" "found: ${azs:-none}"
      fi
    done

    # Every route off the VPC in the table a subnet actually uses — its own, else the main table —
    # as `destination->target`, one per line, or nothing when the only route is the VPC-local one.
    # The target is read from whichever field carries it: an internet gateway sits in `GatewayId`,
    # but a NAT gateway, a transit gateway, a peering connection and an instance each have a field
    # of their own, and reading only the first would count every one of the others as no route.
    #
    # A route whose target has been detached or deleted stays in the table as `blackhole`, still
    # naming the gateway it can no longer reach. It is written out with that state attached, which
    # is what keeps the two tiers reading it correctly: for the public tier it is not egress, and
    # for the private tier it is still a rule somebody added and worth naming.
    routes_off_vpc() {
      jq -r --arg subnet "$1" '
        (first(.RouteTables[] | select(any(.Associations[]?; .SubnetId == $subnet)))
          // first(.RouteTables[] | select(any(.Associations[]?; .Main == true))))
        | .Routes[]? | select(.GatewayId != "local")
        | "\(.DestinationCidrBlock // .DestinationIpv6CidrBlock // .DestinationPrefixListId)->\(
            .GatewayId // .NatGatewayId // .TransitGatewayId // .VpcPeeringConnectionId
            // .NetworkInterfaceId // .InstanceId // .EgressOnlyInternetGatewayId // "unknown")\(
            if .State == "active" then "" else " (\(.State // "unknown"))" end)"' \
        <<<"$route_tables"
    }
    public_out=0 private_out=0 private_leaks=""
    while read -r subnet_id; do
      [[ -n "$subnet_id" ]] || continue
      # Whole-line, so a blackholed gateway — which carries the state as a suffix — does not match.
      if routes_off_vpc "$subnet_id" | grep -qx '0\.0\.0\.0/0->igw-[a-z0-9]\{1,\}'; then
        public_out=$((public_out + 1))
      fi
    done < <(tier public SubnetId)
    while read -r subnet_id; do
      [[ -n "$subnet_id" ]] || continue
      leaks="$(routes_off_vpc "$subnet_id" | tr '\n' ' ')"
      if [[ -n "$leaks" ]]; then
        private_out=$((private_out + 1))
        private_leaks+="${subnet_id}: ${leaks% }; "
      fi
    done < <(tier private SubnetId)

    if [[ "$public_out" -ge 2 ]]; then
      pass "both public subnets route 0.0.0.0/0 to an internet gateway"
    else
      fail "both public subnets route 0.0.0.0/0 to an internet gateway" \
        "${public_out} do with a live gateway — a Fargate task cannot pull its image, fetch its secret or start at all without one"
    fi
    if [[ "$private_out" -eq 0 ]]; then
      pass "no private subnet has any route off the VPC"
    else
      fail "no private subnet has any route off the VPC" \
        "${private_out} do — the database tier is not isolated: ${private_leaks%; }"
    fi
  fi

  if group_azs="$(aws_read rds describe-db-subnet-groups --region "$REGION" \
    --db-subnet-group-name compass \
    --query 'DBSubnetGroups[0].Subnets[].SubnetAvailabilityZone.Name' --output text)" \
    && [[ "$(tr '\t' '\n' <<<"$group_azs" | sort -u | wc -l)" -ge 2 ]]; then
    pass "the db subnet group spans two availability zones"
  else
    fail "the db subnet group spans two availability zones" \
      "RDS requires two even for a single-AZ instance, and rejects the group mid-apply otherwise"
  fi
fi

# 9 — The chain. Each group must admit exactly one upstream: a second rule is not a smaller
#     mistake than a wrong one, because the whole guarantee is "only through here".
step "9. Each security group admits exactly one upstream (catches: a widened rule anywhere in the chain)"
group_id() {
  aws_read ec2 describe-security-groups --region "$REGION" \
    --filters "Name=group-name,Values=compass-$1" "Name=vpc-id,Values=${vpc_id:-none}" \
    --query 'SecurityGroups[0].GroupId' --output text
}
group_json() {
  aws_read ec2 describe-security-groups --region "$REGION" --group-ids "$1" \
    --query 'SecurityGroups[0]' --output json
}

# Exactly one ingress rule, TCP, on exactly the expected port, from exactly one source — `$4` being
# a security group id or the literal `prefix-list`. "Exactly one" is the assertion, not a tidiness
# preference: a second source is the whole guarantee gone, whatever it happens to be.
assert_sole_ingress() {
  local label="$1" json="$2" port="$3" expected="$4" consequence="$5"
  local count rule proto admits sources only_source list_name

  count="$(jq -r '.IpPermissions | length' <<<"$json")"
  if [[ "$count" != "1" ]]; then
    fail "the ${label} group has exactly one ingress rule" "it has ${count} — ${consequence}"
    return
  fi

  rule="$(jq -c '.IpPermissions[0]' <<<"$json")"

  # Protocol before ports: `-1` (all traffic) carries no ports at all, and a UDP rule on the right
  # port from the right source is still a rule the chain never asked for.
  proto="$(jq -r '.IpProtocol' <<<"$rule")"
  if [[ "$proto" != "tcp" ]]; then
    fail "the ${label} group admits TCP only" "its rule's protocol is ${proto} — every link in the chain is a TCP connection"
    return
  fi

  admits="$(jq -r '"\(.FromPort)-\(.ToPort)"' <<<"$rule")"
  if [[ "$admits" != "${port}-${port}" ]]; then
    fail "the ${label} group admits port ${port} only" "it admits ${admits}"
    return
  fi

  # Every source, from all four lists, flattened — so a CIDR sitting alongside the expected group
  # is a failure rather than something the next line looks past.
  sources="$(jq -r '[.IpRanges[].CidrIp, .Ipv6Ranges[].CidrIpv6, .PrefixListIds[].PrefixListId, .UserIdGroupPairs[].GroupId] | join(",")' <<<"$rule")"
  if [[ -z "$sources" || "$sources" == *,* ]]; then
    fail "the ${label} group admits exactly one source on ${port}" \
      "it admits ${sources:-nothing} — ${consequence}"
    return
  fi
  only_source="$sources"

  if [[ "$expected" == "prefix-list" ]]; then
    list_name="$(aws_read ec2 describe-managed-prefix-lists --region "$REGION" \
      --prefix-list-ids "$only_source" --query 'PrefixLists[0].PrefixListName' --output text)" || list_name=""
    if [[ "$list_name" == "com.amazonaws.global.cloudfront.origin-facing" ]]; then
      pass "the ${label} group admits ${list_name} on ${port}, and nothing else"
    else
      fail "the ${label} group admits CloudFront's origin-facing prefix list on ${port}" \
        "it admits ${list_name:-$only_source} — ${consequence}"
    fi
  elif [[ "$only_source" == "$expected" ]]; then
    pass "the ${label} group admits ${expected} on ${port}, and nothing else"
  else
    fail "the ${label} group admits ${expected} on ${port}" "it admits ${only_source} — ${consequence}"
  fi
}

alb_sg="$(group_id alb || true)"
task_sg="$(group_id task || true)"
db_sg="$(group_id database || true)"
if [[ -z "$alb_sg" || -z "$task_sg" || -z "$db_sg" ]]; then
  fail "all three security groups exist" "found alb=${alb_sg:-none} task=${task_sg:-none} database=${db_sg:-none}"
else
  assert_sole_ingress "load balancer" "$(group_json "$alb_sg")" 443 "prefix-list" \
    "the path-routing arrangement can then be bypassed by reaching the origin directly"
  assert_sole_ingress "task" "$(group_json "$task_sg")" "$CONTAINER_PORT" "$alb_sg" \
    "the task is then reachable without passing the load balancer"
  db_json="$(group_json "$db_sg")"
  assert_sole_ingress "database" "$db_json" "$DATABASE_PORT" "$task_sg" \
    "the database is then reachable by something other than the API"

  # A database never opens a connection of its own, so egress here is always something added
  # rather than something needed.
  db_egress="$(jq -r '.IpPermissionsEgress | length' <<<"$db_json")"
  if [[ "$db_egress" == "0" ]]; then
    pass "the database group initiates nothing outbound"
  else
    fail "the database group initiates nothing outbound" \
      "it has ${db_egress} egress rule(s), which nothing in the design needs"
  fi
fi

# 10 — The image. "It is in ECR" is not enough: an image built for the wrong architecture pushes
#      perfectly and then fails to pull, before any application log exists to look at.
step "10. ECR holds an image built for the architecture the task will run (catches: a task that never pulls)"
# An existing but empty repository answers with `null` rather than failing, so both are the same
# assertion: there is nothing here for a task definition to name.
#
# Untagged entries are filtered out *before* the sort, not after. A push lists more than one
# manifest: the tagged index, and under it the per-architecture image and BuildKit's attestation as
# untagged entries pushed a moment earlier — and an interrupted push leaves the same residue with no
# index on top. None of those can be named in a task definition, so "the newest image" means the
# newest one that has a tag.
if ! newest="$(aws_read ecr describe-images --region "$REGION" --repository-name "$ECR_REPOSITORY" \
  --query 'sort_by(imageDetails[?imageTags], &imagePushedAt)[-1]' --output json)" || [[ "$newest" == "null" ]]; then
  fail "the ${ECR_REPOSITORY} repository holds a tagged image" \
    "describe-images found no tagged image, or the repository does not exist"
else
  image_tag="$(jq -r '.imageTags[0]' <<<"$newest")"
  pass "${ECR_REPOSITORY}:${image_tag} was pushed $(jq -r '.imagePushedAt' <<<"$newest")"

  # The architecture lives in the image's config blob, not in its manifest, so a single-platform
  # push takes a presigned fetch to read. An index carries it inline instead.
  #
  # ⚠️ BuildKit attaches provenance and SBOM attestations to the index as extra manifests whose
  # platform is literally `unknown/unknown`. They are not images and nothing ever pulls them, so
  # they are filtered out here — left in, a perfectly good arm64 image reports "arm64,unknown".
  manifest="$(aws_read ecr batch-get-image --region "$REGION" --repository-name "$ECR_REPOSITORY" \
    --image-ids "imageDigest=$(jq -r '.imageDigest' <<<"$newest")" \
    --query 'images[0].imageManifest' --output text)" || manifest=""
  arches=""
  if [[ -n "$manifest" ]]; then
    arches="$(jq -r '[.manifests[]? | select(.platform.architecture != "unknown" and .platform.os != "unknown")
      | .platform.architecture] | unique | join(" ")' <<<"$manifest" 2>/dev/null || true)"
    if [[ -z "$arches" ]]; then
      config_digest="$(jq -r '.config.digest // empty' <<<"$manifest" 2>/dev/null || true)"
      if [[ -n "$config_digest" ]] && config_url="$(aws_read ecr get-download-url-for-layer \
        --region "$REGION" --repository-name "$ECR_REPOSITORY" --layer-digest "$config_digest" \
        --query downloadUrl --output text)"; then
        arches="$(curl -sS --max-time 20 "$config_url" | jq -r '.architecture // empty' || true)"
      fi
    fi
  fi
  # Membership, not equality: what decides whether the task can pull is whether the expected
  # architecture is *offered*. A deliberately multi-arch push is not a fault.
  if [[ " ${arches} " == *" ${EXPECTED_ARCH} "* ]]; then
    pass "the image offers ${EXPECTED_ARCH} (manifest: ${arches})"
  else
    fail "the image offers ${EXPECTED_ARCH}" \
      "it offers '${arches:-unreadable}' — the task definition's cpu_architecture must match one of these, or the pull fails with no application log"
  fi
fi

# 11 — The other half of the origin lock. CloudFront's edge ranges are published and shared, so
#      the prefix list alone proves "a CloudFront", not "this distribution". If these two values
#      ever drift apart, every request through CloudFront is rejected at the listener — a total
#      API outage whose cause is in neither the app's logs nor the load balancer's health checks.
step "11. CloudFront sends the origin header the secret holds (catches: a locked-out origin, or an unlocked one)"
if ! secret="$(aws_read secretsmanager get-secret-value --region "$REGION" \
  --secret-id "$ORIGIN_SECRET_NAME" --query SecretString --output text)"; then
  fail "the secret ${ORIGIN_SECRET_NAME} can be read by name" \
    "the app stack reads it by name, never through remote state, so this name is the coupling"
elif [[ -z "${cfg:-}" ]]; then
  fail "the distribution's origin header can be checked" "the distribution configuration was not readable above"
else
  pass "the secret ${ORIGIN_SECRET_NAME} exists and holds a value"
  header_name="$(jq -r '.header_name // empty' <<<"$secret")"
  header_value="$(jq -r '.header_value // empty' <<<"$secret")"
  sent="$(jq -r --arg host "$ORIGIN_HOSTNAME" --arg name "$header_name" \
    '.DistributionConfig.Origins.Items[] | select(.DomainName == $host)
     | .CustomHeaders.Items[]? | select(.HeaderName == $name) | .HeaderValue' <<<"$cfg")"
  if [[ -z "$header_name" || -z "$header_value" ]]; then
    fail "the secret holds {header_name, header_value}" "one or both keys are missing"
  elif [[ -z "$sent" ]]; then
    fail "CloudFront adds ${header_name} to origin requests" \
      "no such custom header on the ${ORIGIN_HOSTNAME} origin — anything that can reach the ALB from a CloudFront range gets in"
  elif [[ "$sent" == "$header_value" ]]; then
    pass "CloudFront sends ${header_name} with the value the secret holds"
  else
    fail "CloudFront sends ${header_name} with the value the secret holds" \
      "the two have drifted — the listener rule would reject every request CloudFront forwards"
  fi
fi

# 12. The Finnhub key is Terraform's by name and the operator's by value (ADR 0010). Both halves
#     fail late and far away: a missing secret stops the *app* stack's plan, and a placeholder
#     starts a healthy task whose every quote comes back 401. The value is compared, never printed.
step "12. The Finnhub API key has been set by hand (catches: a deployed API calling Finnhub with the placeholder)"
if ! finnhub_key="$(aws_read secretsmanager get-secret-value --region "$REGION" \
  --secret-id "$FINNHUB_SECRET_NAME" --query SecretString --output text)"; then
  fail "the secret ${FINNHUB_SECRET_NAME} can be read by name" \
    "the app stack injects it into the task by name — apply the foundation stack to create it"
elif [[ -z "$finnhub_key" || "$finnhub_key" == "$FINNHUB_SECRET_PLACEHOLDER" ]]; then
  fail "the secret ${FINNHUB_SECRET_NAME} holds a key" \
    "it still holds Terraform's placeholder — set it with the put-secret-value command in infra/README.md"
else
  pass "the secret ${FINNHUB_SECRET_NAME} holds a value that is not the placeholder"
fi

printf '\n'
if [[ "$failures" -eq 0 ]]; then
  printf '\033[32mThe foundation stack is up and the front door is proven.\033[0m\n'
  exit 0
fi
printf '\033[31m%d assertion(s) failed.\033[0m\n' "$failures"
exit 1
