# Security here is a chain, not a perimeter (#10, ADR 0009). Each group admits exactly one
# upstream and nothing else:
#
#     CloudFront ──443──▶ load balancer ──8080──▶ task ──5432──▶ database
#
# Read as a sentence: the load balancer admits only CloudFront's edge fleet, the task admits only
# the load balancer, and the database admits only the task. Nothing in the chain admits a CIDR, so
# there is no rule anywhere that a later "just for a moment" edit can widen to 0.0.0.0/0 without
# it being obvious in the diff.
#
# The groups persist here while the resources they attach to do not, which is fine — a security
# group is free and references nothing outside the VPC. It also means the chain is a fixed fact the
# ephemeral stack inherits rather than re-derives on every rebuild.
#
# Rules are standalone resources rather than inline `ingress`/`egress` blocks. Inline blocks would
# make the load balancer's group depend on the task's *and* the task's on the load balancer's,
# which is a dependency cycle Terraform cannot plan. Split out, the groups reference nothing and
# the rules reference the groups.

locals {
  # The port the container image already defaults to, restated here because the target group will
  # depend on it silently: get it wrong and every health check fails with nothing in any log to say
  # why. Changing it means changing the image (api/Dockerfile), not only this line.
  container_port = 8080

  database_port = 5432
}

# AWS publishes and maintains the address ranges CloudFront reaches origins from. Pinning the
# managed list rather than a CIDR is what makes "only through CloudFront" stay true as the edge
# fleet grows — and it is only half the lock: the ranges are public, so anyone may connect from
# them. The secret header in origin-secret.tf is the other half, checked by the listener rule the
# app stack owns.
data "aws_ec2_managed_prefix_list" "cloudfront_origin_facing" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}

resource "aws_security_group" "alb" {
  name        = "compass-alb"
  description = "The load balancer: admits CloudFront's origin-facing ranges, reaches only the task"
  vpc_id      = aws_vpc.compass.id

  tags = { Name = "compass-alb" }
}

resource "aws_security_group" "task" {
  name        = "compass-task"
  description = "The ECS task: admits only the load balancer, reaches out for ECR, secrets and logs"
  vpc_id      = aws_vpc.compass.id

  tags = { Name = "compass-task" }
}

resource "aws_security_group" "database" {
  name        = "compass-database"
  description = "The Postgres instance: admits only the task, and initiates nothing"
  vpc_id      = aws_vpc.compass.id

  tags = { Name = "compass-database" }
}

# --- The load balancer -------------------------------------------------------------------------

# HTTPS only. There is no port 80 rule to redirect from: CloudFront reaches the origin over HTTPS,
# and a plaintext listener would be a second door putting the session cookie on the wire.
resource "aws_vpc_security_group_ingress_rule" "alb_from_cloudfront" {
  security_group_id = aws_security_group.alb.id
  description       = "HTTPS from CloudFront's origin-facing ranges only"

  ip_protocol    = "tcp"
  from_port      = 443
  to_port        = 443
  prefix_list_id = data.aws_ec2_managed_prefix_list.cloudfront_origin_facing.id
}

resource "aws_vpc_security_group_egress_rule" "alb_to_task" {
  security_group_id = aws_security_group.alb.id
  description       = "Forwarded requests and health checks to the task"

  ip_protocol                  = "tcp"
  from_port                    = local.container_port
  to_port                      = local.container_port
  referenced_security_group_id = aws_security_group.task.id
}

# --- The task ----------------------------------------------------------------------------------

resource "aws_vpc_security_group_ingress_rule" "task_from_alb" {
  security_group_id = aws_security_group.task.id
  description       = "Requests and health checks from the load balancer only"

  ip_protocol                  = "tcp"
  from_port                    = local.container_port
  to_port                      = local.container_port
  referenced_security_group_id = aws_security_group.alb.id
}

# The one place the chain opens outward, and it has to. The task pulls its image from ECR, fetches
# the database secret and ships logs to CloudWatch over this path — all before the container runs —
# and Phase 1's market-data call will leave the same way. Narrowing it to 443 would break the image
# pull the moment ECR answers on anything else, and narrowing it by address means tracking AWS's
# service ranges by hand. The chain's guarantee is about what may reach in.
resource "aws_vpc_security_group_egress_rule" "task_to_internet" {
  security_group_id = aws_security_group.task.id
  description       = "Image pulls, secret fetches, logs, and later the market-data call"

  ip_protocol = "-1"
  cidr_ipv4   = "0.0.0.0/0"
}

# --- The database ------------------------------------------------------------------------------

resource "aws_vpc_security_group_ingress_rule" "database_from_task" {
  security_group_id = aws_security_group.database.id
  description       = "Postgres from the task only — the migration task uses this same group"

  ip_protocol                  = "tcp"
  from_port                    = local.database_port
  to_port                      = local.database_port
  referenced_security_group_id = aws_security_group.task.id
}

# The database group deliberately declares no egress rule at all. AWS attaches an allow-everything
# egress rule to every new security group, and Terraform revokes it for a group whose configuration
# declares none — which is what is wanted here, because a database instance never opens a
# connection of its own. If a plan ever shows this group *gaining* egress, something else added it.
