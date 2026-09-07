#!/usr/bin/env bash
#
# Answers "is the deployed app working?" in one command, from the laptop, after an apply.
#
# This is the *only* automated seam against the deployed environment, and there is no second one.
# It is an HTTP client rather than a browser on purpose: nothing newly at risk in a deployed
# environment is client-side. What is new is header-level, DNS-level and networking-level — cookie
# attributes, the origin lock, cache behaviour, whether two secrets resolved — and curl asserts on
# those far more precisely than a rendered page can. Client behaviour is already owned by the
# SPA's own seam (#27) and is not re-tested here.
#
# Every assertion is **named for the silent failure it catches**, and they run **cheapest first**,
# so a red line says what broke rather than only that something did. The failures being hunted are
# the ones that present as "the app is broken" with no obvious cause — above all CloudFront
# stripping cookies, where the SPA renders perfectly and every authenticated call 401s.
#
# It cleans up nothing. Teardown is the cleanup (`terraform -chdir=infra/app destroy`), and each
# run mints a fresh unique email so repeated runs never collide on the unique-username constraint.
#
# Needs: bash, curl, jq, and `aws` with a session that can update and describe the ECS service —
# the last assertion forces a deployment, which no HTTP client can do. `terraform` as well when it
# is run bare, because that is where it reads the base URL and the cluster and service from; given
# all three as arguments it never looks.
#
# Usage: scripts/smoke-deployed.sh [base-url] [cluster] [service]
#
# All three are arguments so this can run from CI later without being rewritten. Omitted, they are
# read from the applied stacks, which is what makes the hand-run a bare command.
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

REGION="${REGION:-ap-southeast-2}"

# Two budgets, because the two kinds of request fail differently. Ordinary requests get a generous
# ceiling so a cold edge or a just-started task is not read as an outage. The direct-origin probe
# gets a short one, because there a timeout is the *expected* answer and waiting for it is the
# whole cost of the assertion.
CONNECT_TIMEOUT="${CONNECT_TIMEOUT:-10}"
MAX_TIME="${MAX_TIME:-30}"
ORIGIN_TIMEOUT="${ORIGIN_TIMEOUT:-15}"

for cmd in curl jq; do
  command -v "$cmd" >/dev/null || { echo "missing required command: $cmd" >&2; exit 2; }
done

# --- What to point at ---------------------------------------------------------------------------

base_url="${1:-}"
cluster="${2:-}"
service="${3:-}"

if [[ -z "$base_url" ]]; then
  command -v terraform >/dev/null || { echo "missing required command: terraform (or pass a base URL)" >&2; exit 2; }
  domain="$(terraform -chdir=infra/foundation output -raw domain_name 2>/dev/null || true)"
  [[ -n "$domain" ]] || {
    echo "No base URL given and the foundation stack has no outputs to read one from." >&2
    echo "Pass one: scripts/smoke-deployed.sh https://financial-compass.dev <cluster> <service>" >&2
    exit 2
  }
  base_url="https://${domain}"
fi

if [[ -z "$cluster" || -z "$service" ]]; then
  command -v terraform >/dev/null || { echo "missing required command: terraform (or pass cluster and service)" >&2; exit 2; }
  if ! app_outputs="$(terraform -chdir=infra/app output -json 2>/dev/null)" \
    || [[ "$(jq -r 'length' <<<"$app_outputs")" == "0" ]]; then
    echo "The app stack has no outputs, so it is not applied. Bring it up first — see infra/README.md." >&2
    exit 2
  fi
  cluster="${cluster:-$(jq -r '.cluster_name.value // empty' <<<"$app_outputs")}"
  service="${service:-$(jq -r '.service_name.value // empty' <<<"$app_outputs")}"
  [[ -n "$cluster" && -n "$service" ]] || {
    echo "The app stack has outputs but not cluster_name and service_name, so it is a stack this" >&2
    echo "script does not know how to drive. Re-apply it, or pass them as arguments." >&2
    exit 2
  }
fi

base_url="${base_url%/}"

# ⚠️ The scheme is settled here rather than assumed, because every later line depends on it and two
# of them fail *silently* without it. `${base_url#*://}` is a no-op on a bare hostname, so `apex`
# would keep a scheme it never had; and curl defaults a scheme-less URL to plaintext, so assertion
# 1 would pass green over http while its name says HTTPS. The deployed site is https by
# construction — CloudFront's viewer protocol policy is redirect-to-https — so a missing scheme is
# filled in and a plaintext one is a mistake about what is being smoked, not a mode to support.
case "$base_url" in
  https://*) ;;
  http://*) echo "The base URL is plaintext: ${base_url}. The deployed site is HTTPS only — CloudFront redirects http to https, so a plaintext run would assert against a redirect rather than the app." >&2; exit 2 ;;
  *://*) echo "Unsupported scheme in base URL: ${base_url}" >&2; exit 2 ;;
  *) base_url="https://${base_url}" ;;
esac

apex="${base_url#*://}"
apex="${apex%%/*}"

# The name CloudFront sends /api/* to, which assertion 2 proves is unreachable from here.
#
# Derived from the base URL rather than read out of the foundation stack's `origin_hostname`
# output, deliberately and for the reason `verify-foundation.sh` gives for deriving the same name:
# the point is to check the world rather than the plan, and a probe aimed by the state file cannot
# fail when the state and the world disagree. The override is the escape hatch for a base URL that
# is not the apex this origin belongs to.
ORIGIN_HOSTNAME="${ORIGIN_HOSTNAME:-api-origin.${apex}}"

# The AWS session is a *precondition*, not an assertion, so it is checked here rather than being
# discovered at assertion 9 after eight passes have already been printed. Same exit code as a
# missing command, for the same reason: it is a fact about this laptop, not about the deployment.
command -v aws >/dev/null || { echo "missing required command: aws" >&2; exit 2; }
aws() { command aws --region "$REGION" "$@"; }
if ! aws sts get-caller-identity >/dev/null 2>&1; then
  echo "The AWS session is not usable, most often because it has expired. Sign in again and re-run." >&2
  echo "The last assertion forces a deployment and cannot be done over HTTP alone." >&2
  exit 2
fi

# --- Reporting ----------------------------------------------------------------------------------

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

body_path="${workdir}/body"
headers_path="${workdir}/headers"
jar_a="${workdir}/jar-a"
jar_b="${workdir}/jar-b"

assertion=0
step() { assertion=$((assertion + 1)); printf '\n%d. %s\n' "$assertion" "$1"; }
pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }

# ⚠️ Exits. Unlike `verify-foundation.sh`, which reports on every property of a stack that is
# simply *there*, this walks one session forwards: a failed register leaves nothing to check a
# cookie on, and a failed deployment leaves nothing to re-check a session against. Everything after
# a failure here would be noise, so the first failure is the end of the run.
fail() {
  printf '  \033[31mFAIL\033[0m  %s\n         %s\n' "$1" "${2:-}" >&2
  exit 1
}

# --- Requests -----------------------------------------------------------------------------------

# One request. Leaves the status code in `status`, the body in `$body_path` and the *raw* response
# headers in `$headers_path` — raw because assertion 4 has to read cookie attributes that no cookie
# jar preserves. Returns curl's own exit code so a caller can tell a refusal from an answer.
http() {
  local method="$1" url="$2"
  shift 2
  curl_rc=0
  : >"$body_path"
  : >"$headers_path"
  status="$(curl -sS -X "$method" "$url" \
    -o "$body_path" -D "$headers_path" -w '%{http_code}' \
    --connect-timeout "$CONNECT_TIMEOUT" --max-time "$MAX_TIME" \
    "$@" 2>"${workdir}/curl-err")" || curl_rc=$?
  (( curl_rc == 0 )) || return "$curl_rc"
  refuse_cache_hit "$url"
}

# Why a request never reached an answer. ⚠️ It reads `curl_rc` rather than `$?`, because every
# caller is an `if ! http ...` and inside that block `$?` is the negation's status, not curl's.
curl_failed() {
  printf 'curl exited %s — %s' "$curl_rc" "$(tr -d '\n' <"${workdir}/curl-err" | cut -c1-200)"
}

# Every value of one header from the raw response, lowercased name, CR stripped.
header() {
  tr -d '\r' <"$headers_path" | awk -v want="$1" 'tolower($0) ~ "^" want ":" {
    sub(/^[^:]*:[ \t]*/, ""); print
  }'
}

# Applied to *every* /api/* response, not only assertion 6's, because a cached authenticated
# response is one user's data served to another and it does not matter which path leaks it. This is
# the only cheap witness for that: CloudFront reports the decision in `X-Cache` on every response,
# and the /api/* behaviour is configured with CachingDisabled precisely so it always reads Miss.
refuse_cache_hit() {
  case "$1" in */api/*) ;; *) return 0 ;; esac
  local x_cache
  x_cache="$(header x-cache)"
  if [[ "$x_cache" == *Hit* ]]; then
    fail "no /api/* response is served from CloudFront's cache" \
      "$1 answered with X-Cache: ${x_cache} — the /api/* behaviour's cache policy is not CachingDisabled, and authenticated responses are being served to the wrong users"
  fi
}

# The body as a jq expression, or empty when the body is not JSON at all.
json() { jq -r "$1" <"$body_path" 2>/dev/null || true; }

# --- The account this run uses ------------------------------------------------------------------
#
# Fresh every run, and never deleted: the unique-username constraint is the only thing a repeat run
# could collide on, and a unique email is cheaper than a cleanup path that has to authenticate to
# do its work. `.invalid` is reserved (RFC 2606), so nothing here can ever reach a real inbox.

run_id="$(date -u +%Y%m%d%H%M%S)-$$-${RANDOM}"
email_a="smoke-${run_id}-a@example.invalid"
email_b="smoke-${run_id}-b@example.invalid"
password="smoke-${run_id}-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"

credentials() { jq -nc --arg email "$1" --arg password "$password" '{email: $email, password: $password}'; }

echo "Smoking ${base_url} (service ${service} on ${cluster}, region ${REGION})"
echo "Run ${run_id}"

# 1 — The whole delivery path in one request. Everything below is unreadable if this is red, and
#     it costs one GET, so it goes first.
step "The SPA loads over HTTPS (catches: CloudFront, the bucket or the certificate is dead)"
if ! http GET "$base_url/"; then
  fail "GET ${base_url}/ answers" "$(curl_failed)"
fi
[[ "$status" == "200" ]] || fail "GET ${base_url}/ returns 200" "returned ${status}"
grep -q 'id="root"' "$body_path" \
  || fail "the response is the SPA's index.html" "200, but the body has no #root — the bucket is serving something else"
# The built index.html names fingerprinted bundles under /assets/. The *source* index.html names
# /src/main.tsx, so this is what tells a published build from an unbuilt tree synced by mistake.
grep -q '/assets/' "$body_path" \
  || fail "the SPA is a built bundle" "index.html references nothing under /assets/ — an unbuilt tree was published; run scripts/deploy-spa.sh"
pass "the SPA is served at ${base_url}/"

# 2 — The origin lock, from the one vantage point that can prove it: outside CloudFront's ranges.
#     ⚠️ The security group drops the packet at TCP, so this *times out* rather than being
#     answered. That is why the wait is bounded and why timeout-or-refused is the pass. A 403 is
#     NOT a pass: it is the listener's default action, so it proves the request reached the load
#     balancer and the security-group half of the lock is open.
step "Direct access to ${ORIGIN_HOSTNAME} is refused (catches: the origin is reachable without going through CloudFront)"
origin_rc=0
curl -sS -o /dev/null "https://${ORIGIN_HOSTNAME}/api/health" \
  --connect-timeout "$ORIGIN_TIMEOUT" --max-time "$ORIGIN_TIMEOUT" 2>"${workdir}/curl-err" || origin_rc=$?
case "$origin_rc" in
  28) pass "the connection timed out after ${ORIGIN_TIMEOUT}s — dropped at the security group, as it should be" ;;
  7)  pass "the connection was refused — nothing is listening to anyone but CloudFront" ;;
  6)  fail "${ORIGIN_HOSTNAME} resolves" \
        "DNS does not know the name. Either the app stack's origin record is missing — /api/* then returns an origin error from the edge and every assertion below would fail — or the base URL is not the apex this origin belongs to, in which case set ORIGIN_HOSTNAME." ;;
  0)  fail "${ORIGIN_HOSTNAME} does not answer requests from here" \
        "it answered. Even a 403 is a finding: that comes from the listener's default action, so the request reached the load balancer and the security group is admitting more than CloudFront's origin-facing prefix list" ;;
  *)  fail "the direct-origin probe fails by timing out or being refused" \
        "curl exited ${origin_rc}: $(tr -d '\n' <"${workdir}/curl-err" | cut -c1-200). A TLS-level error is not a pass either — the handshake means TCP was accepted, so the security group already admitted this address." ;;
esac

# 3 — The port the target group depends on silently. It is the cheapest proof that a task is
#     running, that the listener rule still carries the right header, and that the container is
#     listening where the target group expects — none of which appears in any log when it is wrong.
step "GET /api/health returns 200 (catches: the container port, the listener rule, or a redirect the load balancer will loop on)"
if ! http GET "$base_url/api/health"; then
  fail "GET /api/health answers" "$(curl_failed)"
fi
# ⚠️ Exactly 200, and redirects are deliberately not followed, so a 307 shows as a 307. That is the
# one answer this path most needs to reject: the target group matches `200` alone, so a redirect
# here means `UseHttpsRedirection()` has crept back into the API and every health check from the
# load balancer is in a loop the application's own logs say nothing about.
[[ "$status" == "200" ]] || fail "GET /api/health returns 200" \
  "returned ${status}. 307 means UseHttpsRedirection() is back in Program.cs and every load-balancer health check is looping. 502/503 means no healthy target: the task is not running, or the target group's port or health path does not match the container. 403 means the listener rule's header no longer matches CloudFront's."
pass "the origin answers 200 through the public site"

# 4 — Registration, and the cookie attributes read **off the raw Set-Cookie header**.
#     ⚠️ curl's cookie jar does not store SameSite at all, and a jar cannot distinguish "no Domain
#     attribute" from "Domain=<the host we asked>". Both distinctions are exactly what is being
#     asserted, so this reads the header text and never the jar.
step "POST /api/auth/register returns 201 with a correctly scoped session cookie (catches: a cookie the browser will refuse or leak)"
if ! http POST "$base_url/api/auth/register" \
  -H 'Content-Type: application/json' --data "$(credentials "$email_a")" \
  -c "$jar_a"; then
  fail "POST /api/auth/register answers" "$(curl_failed)"
fi
[[ "$status" == "201" ]] || fail "POST /api/auth/register returns 201" \
  "returned ${status}, body: $(head -c 300 "$body_path")"

user_id_a="$(json '.id')"
[[ -n "$user_id_a" && "$user_id_a" != "null" ]] || fail "register returns the new user" "no id in $(head -c 300 "$body_path")"

set_cookie="$(header set-cookie | grep '^compass_auth=' | head -n 1 || true)"
[[ -n "$set_cookie" ]] || fail "the 201 carries a compass_auth cookie" \
  "no Set-Cookie for compass_auth. CloudFront's /api/* behaviour is stripping response cookies — the SPA will render and every authenticated call will 401."
grep -qi '; *HttpOnly' <<<"$set_cookie" || fail "the session cookie is HttpOnly" "Set-Cookie: ${set_cookie}"
grep -qi '; *Secure' <<<"$set_cookie" || fail "the session cookie is Secure" "Set-Cookie: ${set_cookie}"
grep -qi '; *SameSite=Lax' <<<"$set_cookie" || fail "the session cookie is SameSite=Lax" \
  "Set-Cookie: ${set_cookie}. SameSite=Lax is the whole CSRF defence (ADR 0007); there is no antiforgery token behind it."

# The apex, asserted as *scope* rather than as a literal attribute. No Domain attribute is the
# correct expression of it: the browser then scopes the cookie host-only to the name it asked,
# which is the apex, and it is what the app deliberately does not set. A Domain naming the origin
# host is the failure being hunted — the browser drops such a cookie outright, and the symptom is
# a login that appears to succeed and a session that never exists.
cookie_domain="$(sed -n 's/.*; *[Dd]omain= *\([^;]*\).*/\1/p' <<<"$set_cookie" | tr -d ' ')"
if [[ -z "$cookie_domain" ]]; then
  pass "HttpOnly, Secure, SameSite=Lax, and host-only — so scoped to ${apex}"
elif [[ "${cookie_domain#.}" == "$apex" ]]; then
  pass "HttpOnly, Secure, SameSite=Lax, Domain=${cookie_domain}"
else
  fail "the session cookie is scoped to ${apex}" \
    "Domain=${cookie_domain}. A browser refuses a cookie for a domain it did not ask, so the session silently never exists."
fi

# 5 — The big one. Everything above can be green while this is red, and this is where a stripped
#     *request* cookie surfaces: CloudFront drops cookies from origin requests by default, and the
#     result is a site that renders perfectly and 401s on every authenticated call.
step "GET /api/auth/me returns 200 with the cookie (catches: CloudFront stripping cookies — the SPA renders and every authenticated call 401s)"
if ! http GET "$base_url/api/auth/me" -b "$jar_a" -c "$jar_a"; then
  fail "GET /api/auth/me answers" "$(curl_failed)"
fi
[[ "$status" == "200" ]] || fail "GET /api/auth/me returns 200 for a signed-in caller" \
  "returned ${status}. The cookie was set and sent, so the request arrived without it: the /api/* behaviour's origin request policy is not forwarding cookies."
[[ "$(json '.email')" == "$email_a" ]] || fail "the session identifies the user who registered" \
  "expected ${email_a}, got $(json '.email')"
pass "the session round-trips through CloudFront as ${email_a}"

# 6 — A second identity in a second jar. One session proves cookies flow; two prove they are not
#     *shared*, which is the failure a cache in front of an authenticated path produces. The
#     `X-Cache` check is the only cheap witness for it, so it is asserted positively here — absent
#     or Hit both fail — while `refuse_cache_hit` rejects a Hit on every other /api/* response.
step "A second user in a second cookie jar sees themselves, on a cache miss (catches: cache poisoning — one user's response served to another)"
if ! http POST "$base_url/api/auth/register" \
  -H 'Content-Type: application/json' --data "$(credentials "$email_b")" \
  -c "$jar_b"; then
  fail "registering a second user answers" "$(curl_failed)"
fi
[[ "$status" == "201" ]] || fail "registering a second user returns 201" "returned ${status}, body: $(head -c 300 "$body_path")"

if ! http GET "$base_url/api/auth/me" -b "$jar_b" -c "$jar_b"; then
  fail "the second user's session check answers" "$(curl_failed)"
fi
[[ "$status" == "200" ]] || fail "the second user's session check returns 200" "returned ${status}"
me_b="$(json '.email')"
[[ "$me_b" != "$email_a" ]] || fail "the second user is not served the first user's response" \
  "asked as ${email_b} and was answered as ${email_a} — an authenticated response is being cached and replayed"
[[ "$me_b" == "$email_b" ]] || fail "the second user sees themselves" "expected ${email_b}, got ${me_b}"

x_cache="$(header x-cache)"
[[ -n "$x_cache" ]] || fail "the response reports a CloudFront cache decision" \
  "no X-Cache header, so this response did not come through CloudFront and the cache assertion below is unproven"
[[ "$x_cache" == *Miss* ]] || fail "the session check is a cache miss" \
  "X-Cache: ${x_cache} — the /api/* behaviour must use CachingDisabled"
pass "${email_b} sees themselves, X-Cache: ${x_cache}"

# 7 — One round trip, three proofs. There is no cheaper way to learn any of them: a write reaching
#     Postgres means the migration task ran against the database the service is talking to, that
#     `Trust Server Certificate=true` still holds against RDS's certificate, and that both injected
#     secrets resolved. All three fail as a 500 that names none of them.
step "An authenticated write and read back (catches: migrations did not run, certificate trust, or an unresolved secret)"
note_text="smoke ${run_id}"
if ! http POST "$base_url/api/notes" \
  -H 'Content-Type: application/json' --data "$(jq -nc --arg text "$note_text" '{text: $text}')" \
  -b "$jar_a" -c "$jar_a"; then
  fail "POST /api/notes answers" "$(curl_failed)"
fi
[[ "$status" == "201" ]] || fail "POST /api/notes returns 201" \
  "returned ${status}, body: $(head -c 300 "$body_path"). A 500 here is the database: the migration never ran, RDS's certificate is not trusted, or DB_USERNAME/DB_PASSWORD did not resolve."

if ! http GET "$base_url/api/notes" -b "$jar_a" -c "$jar_a"; then
  fail "GET /api/notes answers" "$(curl_failed)"
fi
[[ "$status" == "200" ]] || fail "GET /api/notes returns 200" "returned ${status}"
found="$(jq -r --arg text "$note_text" --arg user "$user_id_a" \
  '[.[] | select(.text == $text and .userId == $user)] | length' <"$body_path" 2>/dev/null || echo 0)"
[[ "$found" == "1" ]] || fail "the note reads back, attributed to its author" \
  "expected exactly one row with that text and userId ${user_id_a}, found ${found} in $(head -c 300 "$body_path")"
pass "the row round-tripped to Postgres and back, attributed to ${email_a}"

# 8 — The signed-out answer the SPA can actually act on. Identity's default is a 302 to
#     /Account/Login, which a `fetch` follows into an HTML page and reports as a confusing parse
#     error rather than as "you are signed out" (ADR 0007). The absent Location header is the
#     assertion; the 401 alone would not catch a redirect that also carried one.
step "An unauthenticated call returns 401 with no Location header (catches: a redirect to a login page an SPA cannot act on)"
if ! http GET "$base_url/api/auth/me"; then
  fail "the signed-out session check answers" "$(curl_failed)"
fi
[[ "$status" == "401" ]] || fail "GET /api/auth/me returns 401 with no cookie" "returned ${status}"
location="$(header location)"
[[ -z "$location" ]] || fail "the 401 carries no Location header" \
  "Location: ${location} — the cookie handler's OnRedirectToLogin is back to Identity's default"
pass "401, no redirect"

# 9 — The most expensive assertion, and the last, because it is the only one that changes anything.
#
#     A forced deployment starts a *second* task before the first is stopped — the service runs at
#     desired count 1 with ECS's default minimum-healthy 100% — so the session issued by the old
#     task has to be decrypted by a task that never saw it. That is only possible if the Data
#     Protection key ring is the shared one in Postgres (ADR 0007). Left in memory, every
#     deployment silently signs everyone out, and nothing anywhere reports it.
#
#     The task ARN is compared before and after so a deployment that rolled nothing cannot pass by
#     re-checking a session against the very task that issued it.
step "The session survives a deployment (catches: an in-memory key ring — every deploy silently signs everyone out)"
before="$(aws ecs list-tasks --cluster "$cluster" --service-name "$service" \
  --desired-status RUNNING --query 'taskArns' --output text 2>/dev/null || true)"
[[ -n "$before" && "$before" != "None" ]] || fail "the service has a running task to replace" \
  "list-tasks found none on ${service}/${cluster} — check the cluster and service arguments"

echo "  forcing a new deployment of ${service}..."
aws ecs update-service --cluster "$cluster" --service "$service" --force-new-deployment >/dev/null \
  || fail "the service accepts a forced deployment" "update-service failed on ${service}/${cluster}"

echo "  waiting for the service to stabilise (this takes a few minutes)..."
aws ecs wait services-stable --cluster "$cluster" --services "$service" \
  || fail "the service reaches a steady state after the deployment" \
    "the waiter gave up or the deployment circuit breaker tripped. The reason is in the stopped task's status (aws ecs describe-tasks), not in the log group, if the container never ran."

after="$(aws ecs list-tasks --cluster "$cluster" --service-name "$service" \
  --desired-status RUNNING --query 'taskArns' --output text 2>/dev/null || true)"
[[ -n "$after" && "$after" != "None" ]] || fail "the service is running a task after the deployment" "list-tasks found none"
[[ "$after" != "$before" ]] || fail "the deployment actually replaced the task" \
  "the same task is running as before, so re-checking the session would prove nothing about a new one"

if ! http GET "$base_url/api/auth/me" -b "$jar_a" -c "$jar_a"; then
  fail "the session check answers after the deployment" "$(curl_failed)"
fi
[[ "$status" == "200" ]] || fail "the session survives a deployment" \
  "returned ${status} from a task that did not issue the cookie. The Data Protection key ring is not the shared one in Postgres, so every deployment signs every user out."
[[ "$(json '.email')" == "$email_a" ]] || fail "the surviving session is still the same user" \
  "expected ${email_a}, got $(json '.email')"
pass "the cookie issued before the deployment is still valid on the new task"

printf '\n\033[32mAll %d assertions passed.\033[0m %s is working.\n' "$assertion" "$base_url"
printf 'Nothing was cleaned up: teardown is the cleanup (terraform -chdir=infra/app destroy).\n'
