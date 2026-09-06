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
  description = "Where the task writes. Destroyed with the stack, so a session's logs do not outlive it."
  value       = aws_cloudwatch_log_group.api.name
}
