data "aws_caller_identity" "current" {}

# Adopted, not created. A `resource` here would let a later `destroy` take the zone with it, and
# the registrar's delegation would then point at name servers that no longer exist.
data "aws_route53_zone" "compass" {
  zone_id = var.hosted_zone_id

  # A wrong zone id would otherwise apply cleanly and put every record in a zone nothing
  # delegates to. Route 53 reports the name with a trailing dot; the comparison drops it.
  lifecycle {
    postcondition {
      condition     = trimsuffix(self.name, ".") == var.domain_name
      error_message = "Hosted zone ${var.hosted_zone_id} serves ${self.name}, not ${var.domain_name}."
    }
  }
}

locals {
  # The fixed name CloudFront sends /api/* to. The app stack owns the record behind it, so
  # this stack points at a *name* rather than at an ALB as a resource — which is what lets the
  # app stack be destroyed and recreated without CloudFront's configuration ever changing.
  origin_hostname = "api-origin.${var.domain_name}"

  spa_bucket_name = "compass-spa-${data.aws_caller_identity.current.account_id}"
}
