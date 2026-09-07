output "origin_hostname" {
  description = "The name CloudFront sends /api/* to, now answered by this stack's load balancer."
  value       = aws_route53_record.origin.fqdn
}

output "load_balancer_dns_name" {
  description = "The load balancer's own name, behind the origin hostname. A request to it directly times out at the security group."
  value       = aws_lb.api.dns_name
}

output "cluster_name" {
  description = "Used to force a new deployment and to wait for stability."
  value       = aws_ecs_cluster.compass.name
}

output "service_name" {
  description = "Used to force a new deployment and to wait for stability."
  value       = aws_ecs_service.api.name
}

output "image" {
  description = "The image this stack is running, tag included. Tags are immutable, so this identifies exactly what is deployed."
  value       = local.image
}

output "log_group" {
  description = "Where all three task definitions write, told apart by stream prefix. Destroyed with the stack, so a session's logs do not outlive it."
  value       = aws_cloudwatch_log_group.compass.name
}

output "database_endpoint" {
  description = "Host and port of the Postgres instance. Resolvable and reachable only from inside the VPC — `scripts/run-one-off-task.sh psql` is the way to query it."
  value       = aws_db_instance.compass.endpoint
}

output "database_secret_arn" {
  description = "The RDS-managed secret holding the master username and password. Its ARN is all this stack knows of it; the value is never read here and never enters state."
  value       = local.database_secret_arn
}

# --- What `scripts/run-one-off-task.sh` needs to place a task where the service runs -------------
#
# Foundation facts, re-published here rather than read from the foundation stack by the script,
# because *this* stack is what decides where its tasks are placed. A one-off task that landed in
# different subnets or a different security group than the service would fail to reach the
# database, and the failure would look like a database fault.

output "task_subnet_ids" {
  description = "The subnets a one-off task must run in, which are the service's."
  value       = local.foundation.public_subnet_ids
}

output "task_security_group_id" {
  description = "The security group a one-off task must run under. It is what the database admits."
  value       = local.foundation.task_security_group_id
}
