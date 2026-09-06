output "domain_name" {
  description = "The apex the SPA is served from."
  value       = var.domain_name
}

output "origin_hostname" {
  description = "The name CloudFront sends /api/* to. The app stack owns the record behind it."
  value       = local.origin_hostname
}

output "hosted_zone_id" {
  description = "The adopted public hosted zone, for the app stack's origin record."
  value       = data.aws_route53_zone.compass.zone_id
}

output "spa_bucket" {
  description = "The private bucket the built SPA is synced into."
  value       = aws_s3_bucket.spa.id
}

output "cloudfront_distribution_id" {
  description = "Used to invalidate the SPA after a deploy."
  value       = aws_cloudfront_distribution.compass.id
}

output "cloudfront_domain_name" {
  description = "The distribution's own name, behind the apex alias."
  value       = aws_cloudfront_distribution.compass.domain_name
}

output "origin_certificate_arn" {
  description = "The ap-southeast-2 certificate the app stack's ALB listener terminates TLS with."
  value       = aws_acm_certificate_validation.origin.certificate_arn
}

# --- What the app stack starts in ---------------------------------------------------------------
#
# These describe the persistent network and the image registry. The one thing deliberately NOT
# output as a value is the origin header: the app stack reads it out of Secrets Manager by name,
# so that a rebuild never has to touch CloudFront. `origin_secret_name` is that name.

output "vpc_id" {
  description = "The VPC the ALB, the task and the database are all placed in."
  value       = aws_vpc.compass.id
}

output "public_subnet_ids" {
  description = "The ALB's subnets, and the task's. Public because a Fargate task with no route out cannot start at all."
  value       = [for subnet in aws_subnet.public : subnet.id]
}

output "private_subnet_ids" {
  description = "The database's subnets. No route off the VPC in either direction."
  value       = [for subnet in aws_subnet.private : subnet.id]
}

output "db_subnet_group_name" {
  description = "Spans two availability zones, which RDS requires even for a single-AZ instance."
  value       = aws_db_subnet_group.compass.name
}

output "alb_security_group_id" {
  description = "Admits CloudFront's origin-facing prefix list on 443 and nothing else."
  value       = aws_security_group.alb.id
}

output "task_security_group_id" {
  description = "Admits the load balancer on the container port and nothing else."
  value       = aws_security_group.task.id
}

output "database_security_group_id" {
  description = "Admits the task on 5432 and nothing else."
  value       = aws_security_group.database.id
}

output "container_port" {
  description = "The port the image listens on. The target group depends on it silently — a wrong value fails every health check with nothing in any log to say why."
  value       = local.container_port
}

output "ecr_repository_url" {
  description = "The registry the task definition names an image in. Tags are immutable."
  value       = aws_ecr_repository.api.repository_url
}

output "origin_secret_name" {
  description = "The Secrets Manager name holding the CloudFront-to-origin header, as {header_name, header_value}. Read it by name; never by remote state."
  value       = aws_secretsmanager_secret.origin_header.name
}
