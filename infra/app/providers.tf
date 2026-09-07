locals {
  default_tags = {
    Project   = "compass"
    Stack     = "app"
    ManagedBy = "terraform"
  }
}

# One provider, one region. There is no us-east-1 alias here: the only certificate CloudFront reads
# belongs to the foundation stack, and everything this stack creates lives in the region the VPC
# does.
provider "aws" {
  region = var.region
  default_tags { tags = local.default_tags }
}
