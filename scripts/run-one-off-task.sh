#!/usr/bin/env bash
#
# Runs one of the app stack's one-off ECS tasks, waits for it to stop, prints what it logged, and
# exits with the container's own exit code.
#
#   scripts/run-one-off-task.sh migrate
#   scripts/run-one-off-task.sh psql -c 'SHOW max_connections'
#
# The first argument is the *kind*, which is both the task definition's suffix (`compass-<kind>`)
# and its container's name — see infra/app/main.tf, where those two are named together for exactly
# this reason. Anything after it replaces the container's command.
#
# ⚠️ The exit code is the point. `aws ecs run-task` succeeds when a task is *accepted*, not when it
# works, so a migration that failed against the database and a migration that never started both
# look like success to it. This waits for the task to stop and then reads `exitCode` out of the
# stopped container, which is why `terraform_data.migrate` can use it as the assertion that
# migrations completed before the service is allowed to serve traffic.
#
# Terraform calls this during an apply and passes everything below as environment variables,
# because the outputs it would otherwise be read from do not exist yet. Run by hand, it reads them
# from the applied stack instead.
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

for cmd in aws jq terraform; do
  command -v "$cmd" >/dev/null || { echo "missing required command: $cmd" >&2; exit 2; }
done

kind="${1:-}"
case "$kind" in
  migrate|psql) shift ;;
  *) echo "usage: scripts/run-one-off-task.sh <migrate|psql> [command...]" >&2; exit 2 ;;
esac

# Read once, so that running this by hand is not four `terraform output` invocations, and so that a
# stack that is torn down says so here rather than in an AWS error about a cluster that is missing.
if [[ -z "${CLUSTER:-}" || -z "${SUBNETS:-}" || -z "${SECURITY_GROUPS:-}" || -z "${LOG_GROUP:-}" ]]; then
  if ! outputs="$(terraform -chdir=infra/app output -json 2>/dev/null)" || [[ "$(jq -r 'length' <<<"$outputs")" == "0" ]]; then
    echo "The app stack has no outputs, so it is not applied. Bring it up first — see infra/README.md." >&2
    exit 1
  fi
  CLUSTER="${CLUSTER:-$(jq -r '.cluster_name.value' <<<"$outputs")}"
  SUBNETS="${SUBNETS:-$(jq -r '.task_subnet_ids.value | join(",")' <<<"$outputs")}"
  SECURITY_GROUPS="${SECURITY_GROUPS:-$(jq -r '.task_security_group_id.value' <<<"$outputs")}"
  LOG_GROUP="${LOG_GROUP:-$(jq -r '.log_group.value' <<<"$outputs")}"
fi

# The foundation and the app stack are both in one region and the app stack's `region` variable
# defaults to it, so this is the same default written once more rather than a second decision.
REGION="${REGION:-ap-southeast-2}"
# By family, so a hand-run uses the latest revision. Terraform passes the exact revision it just
# registered, because during an apply "latest" is a race with itself.
TASK_DEFINITION="${TASK_DEFINITION:-compass-${kind}}"

aws() { command aws --region "$REGION" "$@"; }

# Terraform has already authenticated by the time it calls this, but a hand-run may not have. An
# expired session otherwise arrives as an error about an invalid grant, from whichever call happens
# to be first — possibly after a task has already been started.
if ! aws sts get-caller-identity >/dev/null 2>&1; then
  echo "The AWS session is not usable, most often because it has expired. Sign in again and re-run." >&2
  exit 2
fi

# The container command override, as run-task's `--overrides` document. Absent when no command was
# given, so the task definition's own command stands.
overrides=()
if (( $# > 0 )); then
  # Null-delimited into jq rather than through jq's own `--args`, which goes on parsing what
  # follows it as jq options — so `psql -c 'SHOW max_connections'` would arrive at ECS with the
  # `-c` silently missing and psql would try to connect to a database called SHOW.
  command_json="$(printf '%s\0' "$@" | jq -Rsc 'split("\u0000")[:-1]')"
  overrides=(--overrides "$(jq -nc --arg name "$kind" --argjson command "$command_json" \
    '{containerOverrides: [{name: $name, command: $command}]}')")
fi

echo "Running ${TASK_DEFINITION} on ${CLUSTER}..."

# ⚠️ `startedBy` accepts letters, numbers, hyphens and underscores only, up to 36 characters, so
# the script's own name does not go in it — a slash or a dot is rejected at the API.
started_by="one-off-${kind}"

# The same network position as the service: public subnets and a public address, because a Fargate
# task pulls its image, fetches its secrets and ships its logs over the same path as application
# traffic and cannot start without one (infra/foundation/network.tf). The security group is the
# task's, which is the one the database admits.
run="$(aws ecs run-task \
  --cluster "$CLUSTER" \
  --task-definition "$TASK_DEFINITION" \
  --launch-type FARGATE \
  --started-by "$started_by" \
  --network-configuration "awsvpcConfiguration={subnets=[${SUBNETS}],securityGroups=[${SECURITY_GROUPS}],assignPublicIp=ENABLED}" \
  ${overrides[@]+"${overrides[@]}"} \
  --output json)"

# ⚠️ `run-task` answers 200 with an empty `tasks` and a populated `failures` when it declines to
# place the task — no capacity, a subnet that does not exist, a task definition that is inactive.
# Read as an exit code alone that is a success, and the migration would be reported as done.
task_arn="$(jq -r '.tasks[0].taskArn // empty' <<<"$run")"
if [[ -z "$task_arn" ]]; then
  echo "ECS placed no task, so nothing ran:" >&2
  jq -r '.failures[]? | "  \(.reason) (\(.arn // "no arn"))"' <<<"$run" >&2
  exit 1
fi

task_id="${task_arn##*/}"
echo "  task: ${task_id}"

# `wait tasks-stopped` polls for ten minutes and then gives up. A migration on this schema takes
# seconds; ten minutes means something is wrong in a way that will not resolve itself, so say that
# rather than reporting the waiter's own error.
if ! aws ecs wait tasks-stopped --cluster "$CLUSTER" --tasks "$task_arn"; then
  echo "Gave up waiting for ${task_id} to stop. Look at it with:" >&2
  echo "  aws ecs describe-tasks --region ${REGION} --cluster ${CLUSTER} --tasks ${task_arn}" >&2
  exit 1
fi

task="$(aws ecs describe-tasks --cluster "$CLUSTER" --tasks "$task_arn" --query 'tasks[0]' --output json)"
container="$(jq --arg name "$kind" '.containers[] | select(.name == $name)' <<<"$task")"
exit_code="$(jq -r '.exitCode // empty' <<<"$container")"

# The stream the task definition's awslogs options produce: <prefix>/<container>/<task-id>, and
# both halves of that prefix are the kind. Empty when the container never ran.
if log_events="$(aws logs get-log-events \
      --log-group-name "$LOG_GROUP" \
      --log-stream-name "${kind}/${kind}/${task_id}" \
      --start-from-head --output json 2>/dev/null)"; then
  echo
  jq -r '.events[].message' <<<"$log_events"
  echo
fi

if [[ -z "$exit_code" ]]; then
  # No exit code means the container never ran at all — a pull that failed, a secret the execution
  # role could not fetch, no route out of the subnet. None of those write an application log, so
  # the reason is only in the stopped task's own status.
  echo "The ${kind} container never ran, so it has no exit code." >&2
  echo "  task stopped: $(jq -r '.stoppedReason // "no reason given"' <<<"$task")" >&2
  echo "  container:    $(jq -r '.reason // "no reason given"' <<<"$container")" >&2
  exit 1
fi

if [[ "$exit_code" != "0" ]]; then
  echo "The ${kind} container exited ${exit_code}. Its output is above." >&2
  exit "$exit_code"
fi

echo "${kind} completed."
