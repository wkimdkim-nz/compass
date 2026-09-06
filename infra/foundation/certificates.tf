# Two certificates, in two regions, both validated through the same hosted zone.
#
# Both are free and both persist: a certificate is one of the things this stack exists to keep
# alive across the app stack's teardowns. HTTPS is mandatory here — `.dev` is HSTS-preloaded
# as a whole TLD — so a certificate problem does not present as a warning page a user can click
# past. It presents as a total outage.
#
# Each certificate covers exactly one name and therefore needs exactly one validation record,
# which is why these are four plain resources rather than a `for_each` over
# `domain_validation_options`: that set is unknown at plan time, and a `for_each` over it cannot
# be planned in the same apply that creates the certificate.

# CloudFront's certificate. us-east-1 is not a preference; CloudFront cannot read a certificate
# from anywhere else.
resource "aws_acm_certificate" "cloudfront" {
  provider = aws.us_east_1

  domain_name       = var.domain_name
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_route53_record" "cloudfront_certificate_validation" {
  zone_id = data.aws_route53_zone.compass.zone_id
  name    = one(aws_acm_certificate.cloudfront.domain_validation_options).resource_record_name
  type    = one(aws_acm_certificate.cloudfront.domain_validation_options).resource_record_type
  records = [one(aws_acm_certificate.cloudfront.domain_validation_options).resource_record_value]
  ttl     = 60

  # ACM re-issues against the same validation name, so a replacement certificate must be allowed
  # to write over the record it finds.
  allow_overwrite = true
}

# Blocks the apply until ACM has seen the record above, so nothing attaches a certificate that is
# still PENDING_VALIDATION.
resource "aws_acm_certificate_validation" "cloudfront" {
  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.cloudfront.arn
  validation_record_fqdns = [aws_route53_record.cloudfront_certificate_validation.fqdn]
}

# The ALB's certificate, in the ALB's own region. The ALB itself belongs to the app stack;
# the certificate belongs here, so a teardown does not re-run DNS validation on the next build.
resource "aws_acm_certificate" "origin" {
  domain_name       = local.origin_hostname
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_route53_record" "origin_certificate_validation" {
  zone_id = data.aws_route53_zone.compass.zone_id
  name    = one(aws_acm_certificate.origin.domain_validation_options).resource_record_name
  type    = one(aws_acm_certificate.origin.domain_validation_options).resource_record_type
  records = [one(aws_acm_certificate.origin.domain_validation_options).resource_record_value]
  ttl     = 60

  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "origin" {
  certificate_arn         = aws_acm_certificate.origin.arn
  validation_record_fqdns = [aws_route53_record.origin_certificate_validation.fqdn]
}
