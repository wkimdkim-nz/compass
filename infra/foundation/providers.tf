locals {
  default_tags = {
    Project   = "compass"
    Stack     = "foundation"
    ManagedBy = "terraform"
  }
}

provider "aws" {
  region = var.region
  default_tags { tags = local.default_tags }
}

# CloudFront reads certificates only from N. Virginia, whatever region the rest of the stack runs
# in. This alias exists for that one certificate, and requesting it in `var.region` instead is the
# classic first-attempt failure: the certificate issues fine and CloudFront simply cannot see it.
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
  default_tags { tags = local.default_tags }
}
