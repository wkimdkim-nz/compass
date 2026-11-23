# The second half of the CloudFront lock.
#
# The security-group chain admits CloudFront's origin-facing prefix list, but those ranges are
# published and shared by every CloudFront customer, so "came from a CloudFront edge" is not
# "came from *this* distribution". CloudFront adds this header to every origin request and the app
# stack's listener rule rejects anything arriving without it — which is what closes the path-routing
# bypass that `/api/*` on a same-site subdomain would otherwise leave open (ADR 0009).
#
# It is generated *here*, in the persistent stack, and read by the ephemeral stack **by name** —
# never through a remote-state reference. That mirrors the DNS decoupling exactly: CloudFront's
# configuration must not change when the app stack is destroyed and rebuilt, and a remote-state
# read is a coupling that eventually makes it change. A name is a promise the two stacks can both
# hold without either knowing the other exists.
#
# The header's *name* travels with its value in one JSON document, so the app stack learns both
# from the single lookup. The name is not a secret and does not need to be; carrying it here means
# there is no second constant to keep in step across two stacks.
#
# ⚠️ The container is never configured with any of this. The listener rule enforces it in front of
# the task, and the app makes no AWS calls at all. The header itself still reaches the task
# on the wire — an ALB cannot strip a request header — which is why the API must never log request
# headers.

locals {
  origin_header_name = "X-Origin-Verify"

  # A fixed name, because being fixed is the whole point — see above. It is also the name
  # `scripts/verify-foundation.sh` and the app stack look up.
  origin_secret_name = "compass/cloudfront-origin-header"
}

# 64 characters of base62. `special = false` keeps it to bytes that survive an HTTP header value
# and a shell without quoting rules deciding anything; the entropy is in the length, not the
# alphabet.
resource "random_password" "origin_header" {
  length  = 64
  special = false
}

resource "aws_secretsmanager_secret" "origin_header" {
  name        = local.origin_secret_name
  description = "The header CloudFront adds to origin requests; the ALB listener rule requires it"

  # No recovery window. A deleted secret otherwise lingers for 30 days holding its name hostage,
  # and this secret's name is the coupling between two stacks — being unable to recreate it is a
  # far worse failure than being unable to undelete it. The value is regenerable by definition:
  # `terraform apply` mints a new one and updates CloudFront in the same run.
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "origin_header" {
  secret_id = aws_secretsmanager_secret.origin_header.id

  secret_string = jsonencode({
    header_name  = local.origin_header_name
    header_value = random_password.origin_header.result
  })
}
