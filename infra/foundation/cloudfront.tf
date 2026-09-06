# One distribution, two origins, and the whole reason the SPA and the API share an origin.
#
# The SPA is served at the root and /api/* is path-routed to the origin hostname, so the browser
# treats them as one site and attaches the session cookie by itself. This arrangement is
# load-bearing, not a convenience: an `api.` subdomain would be same-*site* but cross-*origin*,
# which forces a CORS policy with AllowCredentials and drags antiforgery back in (ADR 0007, ADR
# 0009). Do not "simplify" it.

data "aws_cloudfront_cache_policy" "caching_optimized" {
  name = "Managed-CachingOptimized"
}

data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

# Forwards every header except Host, plus all cookies and query strings. The cookie half is what
# matters: CloudFront strips cookies from origin requests by default, and when it does the SPA
# renders perfectly while every authenticated call 401s — the single most likely cause of "the
# deployed app is broken".
data "aws_cloudfront_origin_request_policy" "all_viewer_except_host_header" {
  name = "Managed-AllViewerExceptHostHeader"
}

resource "aws_cloudfront_function" "spa_router" {
  name    = "compass-spa-router"
  runtime = "cloudfront-js-2.0"
  comment = "Rewrites client-side routes to /index.html"
  code    = file("${path.module}/spa-router.js")
  publish = true
}

locals {
  spa_origin_id = "spa-bucket"
  api_origin_id = "api-origin"
}

resource "aws_cloudfront_distribution" "compass" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "Compass — SPA at the root, /api/* to the origin hostname"
  default_root_object = "index.html"
  aliases             = [var.domain_name]

  # The developer and the users are in New Zealand; the cheaper price classes exclude Oceania's
  # edge locations. At this traffic volume the difference is cents.
  price_class = "PriceClass_All"

  origin {
    origin_id                = local.spa_origin_id
    domain_name              = aws_s3_bucket.spa.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.spa.id
  }

  # A name, not a resource reference. Nothing answers here until the ephemeral stack creates the
  # record and the ALB behind it; until then /api/* returns an origin error, which is correct.
  origin {
    origin_id   = local.api_origin_id
    domain_name = local.origin_hostname

    custom_origin_config {
      http_port  = 80
      https_port = 443
      # HTTPS only: CloudFront reaches the origin across the public internet, and plain HTTP would
      # put the session cookie on the wire in clear text.
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }

    # The CloudFront-to-origin secret header is added here by #42, once the foundation stack
    # generates it into Secrets Manager.
  }

  default_cache_behavior {
    target_origin_id       = local.spa_origin_id
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true
    cache_policy_id        = data.aws_cloudfront_cache_policy.caching_optimized.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.spa_router.arn
    }
  }

  ordered_cache_behavior {
    path_pattern           = "/api/*"
    target_origin_id       = local.api_origin_id
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true

    # Caching disabled and cookies forwarded, together. Either one alone is a security bug: a
    # cached authenticated response is one user's data served to another.
    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer_except_host_header.id
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate_validation.cloudfront.certificate_arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }
}

# The apex needs an alias, because a CNAME at a zone apex is illegal. Aliases are also free, where
# every query against a CNAME would be billed.
resource "aws_route53_record" "apex" {
  for_each = toset(["A", "AAAA"])

  zone_id = data.aws_route53_zone.compass.zone_id
  name    = var.domain_name
  type    = each.value

  alias {
    name                   = aws_cloudfront_distribution.compass.domain_name
    zone_id                = aws_cloudfront_distribution.compass.hosted_zone_id
    evaluate_target_health = false
  }
}
