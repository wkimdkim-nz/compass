# The built SPA lives in a private bucket. It is never a website endpoint and never public: the
# only reader is CloudFront, holding an origin access control signature, which is also what makes
# "served over HTTPS" enforceable — an S3 website endpoint speaks plain HTTP.

resource "aws_s3_bucket" "spa" {
  bucket = local.spa_bucket_name
}

resource "aws_s3_bucket_public_access_block" "spa" {
  bucket = aws_s3_bucket.spa.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# CloudFront reads the bucket as itself, signing each request with SigV4, so the bucket needs no
# public path and no origin access identity user.
resource "aws_cloudfront_origin_access_control" "spa" {
  name                              = "compass-spa"
  description                       = "CloudFront to the private SPA bucket"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

data "aws_iam_policy_document" "spa_bucket" {
  statement {
    sid       = "AllowCloudFrontRead"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.spa.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    # Scoped to this one distribution, so the grant is to a resource rather than to the service.
    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.compass.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "spa" {
  bucket = aws_s3_bucket.spa.id
  policy = data.aws_iam_policy_document.spa_bucket.json

  # Ordered explicitly: the block is what makes attaching a policy safe, and Terraform would
  # otherwise be free to create the two in either order.
  depends_on = [aws_s3_bucket_public_access_block.spa]
}
