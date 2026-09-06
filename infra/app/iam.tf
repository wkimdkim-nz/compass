# One role, and the deliberate absence of a second.
#
# ECS distinguishes two roles and confusing them is the usual mistake. The **execution role** is
# assumed by the ECS agent, before the container runs, to pull the image, ship logs, and — from
# #44 — fetch the database secret. The **task role** is assumed by the application itself, for AWS
# calls it makes at runtime.
#
# ⚠️ There is no task role here, and its absence is the design rather than an omission. Compass
# makes zero AWS calls: credentials arrive as environment variables the agent injected, so there is
# no SDK in the image and nothing for a task role to hold (#19). Omitting it makes that structural
# — a task with no role cannot acquire one by someone adding a policy to it — and it is why the
# secret header is enforced at the listener and never handed to the container.

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

# ECR pulls and CloudWatch Logs, which is the whole of what this stack asks of it. #44 adds an
# inline policy for the one secret ARN it fetches; nothing else is ever attached here.
resource "aws_iam_role_policy_attachment" "task_execution" {
  role       = aws_iam_role.task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}
