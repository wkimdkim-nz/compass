# One role, and the deliberate absence of a second.
#
# ECS distinguishes two roles and confusing them is the usual mistake. The **execution role** is
# assumed by the ECS agent, before the container runs, to pull the image, ship logs, and fetch the
# two secrets. The **task role** is assumed by the application itself, for AWS calls it makes
# at runtime.
#
# ⚠️ There is no task role here, and its absence is the design rather than an omission. Compass
# makes zero AWS calls: credentials arrive as environment variables the agent injected, so there is
# no SDK in the image and nothing for a task role to hold. Omitting it makes that structural
# — a task with no role cannot acquire one by someone adding a policy to it — and it is why the
# secret header is enforced at the listener rather than by anything the container is given.

data "aws_iam_policy_document" "task_execution_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "task_execution" {
  name               = "compass-task-execution"
  description        = "Assumed by the ECS agent to pull the image and ship logs. The application assumes nothing."
  assume_role_policy = data.aws_iam_policy_document.task_execution_assume.json
}

# ECR pulls and CloudWatch Logs, which is what every task here needs before its container runs.
# The one thing it does not cover is the two secrets the agent injects; those are the two inline
# policies below, and nothing else is ever attached to this role.
resource "aws_iam_role_policy_attachment" "task_execution" {
  role       = aws_iam_role.task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# The database secret, named by its ARN rather than by a wildcard or a path prefix. With the Finnhub
# key below, the blast radius of this deployment is exactly two credentials — which is the whole of
# the argument for having the agent fetch them rather than the application.
#
# ⚠️ No `kms:Decrypt` statement, and none is needed: RDS encrypts the managed secret with the
# AWS-managed `aws/secretsmanager` key, which grants use through the calling service. A
# customer-managed key would need `kms:Decrypt` on the key here, and the failure without it is a
# task that stops before the container runs (rds.tf).
data "aws_iam_policy_document" "task_execution_database_secret" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [local.database_secret_arn]
  }
}

resource "aws_iam_role_policy" "task_execution_database_secret" {
  name   = "compass-database-secret"
  role   = aws_iam_role.task_execution.id
  policy = data.aws_iam_policy_document.task_execution_database_secret.json
}

# The Finnhub API key, which lives in the foundation stack and is found by name (main.tf). Its own
# policy rather than a second statement in the one above, so that each is still one ARN and one
# reason. It is encrypted with the same AWS-managed key, so again no `kms:Decrypt`.
data "aws_iam_policy_document" "task_execution_finnhub_secret" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [data.aws_secretsmanager_secret.finnhub_api_key.arn]
  }
}

resource "aws_iam_role_policy" "task_execution_finnhub_secret" {
  name   = "compass-finnhub-secret"
  role   = aws_iam_role.task_execution.id
  policy = data.aws_iam_policy_document.task_execution_finnhub_secret.json
}
