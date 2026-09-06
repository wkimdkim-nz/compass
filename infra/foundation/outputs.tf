output "domain_name" {
  description = "The apex the SPA is served from."
  value       = var.domain_name
}

output "origin_hostname" {
  description = "The name CloudFront sends /api/* to. The ephemeral stack owns the record behind it."
  value       = local.origin_hostname
}

output "hosted_zone_id" {
  description = "The adopted public hosted zone, for the ephemeral stack's origin record."
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
  description = "The ap-southeast-2 certificate the ephemeral stack's ALB listener terminates TLS with."
  value       = aws_acm_certificate_validation.origin.certificate_arn
}
