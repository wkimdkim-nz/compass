# The joint between the two stacks, and the reason it is a name rather than a reference.
#
# CloudFront's `/api/*` origin is the string `api-origin.financial-compass.dev`, fixed in the
# foundation stack. This record is what that name points at, and it lives *here*, so it is created
# with the load balancer and destroyed with it. CloudFront's configuration never changes when the
# ephemeral stack is rebuilt, and the site keeps serving the SPA while it is torn down — `/api/*`
# simply returns an origin error, which is the correct result rather than a fault.
#
# ⚠️ This record must never be moved to the foundation stack. Left behind after a destroy, it
# points at a load balancer hostname AWS has taken back, on a subdomain of the site itself — and
# `SameSite=Lax` treats every `*.financial-compass.dev` as same-site, so whoever next receives that
# hostname can forge authenticated requests (ADR 0007's subdomain precondition, ADR 0009).

resource "aws_route53_record" "origin" {
  zone_id = local.foundation.hosted_zone_id
  name    = local.foundation.origin_hostname

  # A only. The load balancer is IPv4-only, as is the VPC it sits in — the foundation stack's
  # network is deliberately single-stack — so an AAAA alias would resolve to nothing.
  type = "A"

  # An alias, not a CNAME: aliases are free where every query against a CNAME is billed, and the
  # load balancer's address is not stable enough to write down.
  alias {
    name    = aws_lb.api.dns_name
    zone_id = aws_lb.api.zone_id

    # There is one target and no failover to fall back to, so health-evaluating the alias would
    # only change an origin error into a resolution failure. Health is the target group's job.
    evaluate_target_health = false
  }
}
