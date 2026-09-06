# The load balancer, and the lock that makes CloudFront the only way through it.
#
# The lock has two halves and neither is sufficient alone. The security group — owned by the
# foundation stack — admits only AWS's managed `com.amazonaws.global.cloudfront.origin-facing`
# prefix list, which proves "a CloudFront". Those ranges are published and shared by every
# CloudFront customer, so the listener below proves "*this* distribution": its default action
# refuses everything, and a single rule forwards only requests carrying the secret header that the
# foundation stack generated and CloudFront adds to every origin request.
#
# ⚠️ A direct request to the origin hostname from anywhere else never reaches the listener at all.
# The security group drops it at TCP, so it **times out** rather than being answered — which is why
# every check of this property needs a bounded wait, and why timeout-or-403 is the pass rather than
# 403 alone.

resource "aws_lb" "api" {
  name               = "compass-api"
  load_balancer_type = "application"

  # Internet-facing, and it has to be: CloudFront reaches origins across the public internet. What
  # keeps that safe is the two-part lock above, not the absence of a public address.
  internal        = false
  subnets         = local.foundation.public_subnet_ids
  security_groups = [local.foundation.alb_security_group_id]

  # Stated, because dns.tf relies on it: the origin record is an A alias and nothing else, so a
  # dualstack load balancer would advertise AAAA addresses nothing resolves to. The VPC is IPv4
  # only in any case.
  ip_address_type = "ipv4"

  # Stated rather than defaulted, for the same reason RDS's deletion protection will be: this stack
  # exists to be destroyed, and a protected load balancer turns the cost lever into a support
  # ticket. It is off by default; saying so here means a later change to it is a visible diff.
  enable_deletion_protection = false
}

# `ip` targets, not `instance`: an awsvpc task has its own elastic network interface and is
# registered by address. There is no instance to register — Fargate has no EC2 host to name.
resource "aws_lb_target_group" "api" {
  name        = "compass-api"
  vpc_id      = local.foundation.vpc_id
  target_type = "ip"

  # Plain HTTP on the last hop, deliberately. TLS is terminated at the listener above, the hop runs
  # inside the VPC between two security groups that admit only each other, and the API must never
  # register `UseHttpsRedirection()` — it would answer this check with a 307 and the service would
  # never come up (ADR 0009, and the comment saying so in api/Compass.Api/Program.cs).
  protocol = "HTTP"
  port     = local.container_port

  health_check {
    # ⚠️ `/api/health`, not `/health`. CloudFront path-routes only `/api/*` to the origin, so the
    # health path lives under that prefix — see the remarks on HealthEndpoints. Nothing answers on
    # the shorter path, and a target group pointed at it fails every check.
    path     = "/api/health"
    protocol = "HTTP"

    # Exactly 200. The band `200-399` would count a redirect as healthy, and a redirect is the one
    # answer this check most needs to reject.
    matcher = "200"

    interval            = 15
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }

  # The default is 300s, which is five minutes of a `destroy` waiting on a task that has already
  # stopped answering. Nothing here holds long-lived connections worth draining.
  deregistration_delay = 30
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.api.arn
  port              = 443
  protocol          = "HTTPS"

  # Issued in this region by the foundation stack, for the origin hostname the record below points
  # at. CloudFront verifies it by SNI on every origin request, so a certificate from the wrong
  # region — or for the wrong name — presents as every `/api/*` request failing at the edge.
  certificate_arn = local.foundation.origin_certificate_arn

  # TLS 1.3 and 1.2. CloudFront is pinned to TLSv1.2 at the origin end, so this is the floor rather
  # than the negotiated version.
  ssl_policy = "ELBSecurityPolicy-TLS13-1-2-2021-06"

  # The lock's default position: refuse. Everything that is *not* proven to come from this
  # distribution ends here, having reached nothing. The rule below is the only way past it, so a
  # rule that is deleted or mis-conditioned fails closed.
  #
  # 403 rather than 404, so a check of the origin lock has an unambiguous answer to look for when
  # it does reach the listener — the far more common outcome is a timeout at the security group.
  default_action {
    type = "fixed-response"

    fixed_response {
      content_type = "text/plain"
      message_body = "Forbidden.\n"
      status_code  = "403"
    }
  }
}

# The second half of the origin lock. Everything else about reaching the API is a network fact;
# this is the only assertion of identity, and it is checked in front of the task rather than by it.
#
# ⚠️ If this value and CloudFront's ever drift apart, every request through the public site is
# refused here — a total API outage with nothing in the application's logs and nothing in the
# target group's health, because both are healthy and neither is involved.
#
# ⚠️ The value is sensitive, so Terraform hides the whole `condition` block from every plan — the
# header's *name* included, exactly as it hides CloudFront's origin block at the other end. A clean
# plan is therefore not evidence that this rule still says what it should. The only honest check is
# a request through the public site, which is what `/api/health` returning 200 proves.
resource "aws_lb_listener_rule" "cloudfront" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 1

  condition {
    http_header {
      http_header_name = local.origin_header.header_name
      values           = [local.origin_header.header_value]
    }
  }

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}
