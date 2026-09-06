terraform {
  required_version = ">= 1.11.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }

    # Generates the CloudFront-to-origin header value. Its result lives in state — which is the
    # trade this one secret makes and the reason the *database* password is left to
    # `manage_master_user_password` instead (#19).
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # State lives in S3 and never in git. The bucket cannot be created by the configuration whose
  # state it holds, so `scripts/bootstrap-state-bucket.sh` creates it once, before the first init.
  #
  # `use_lockfile` is S3-native locking (Terraform 1.11+): a companion `.tflock` object in the same
  # bucket. There is no DynamoDB table to pay for or forget.
  backend "s3" {
    bucket       = "compass-tfstate-625495455169"
    key          = "foundation/terraform.tfstate"
    region       = "ap-southeast-2"
    encrypt      = true
    use_lockfile = true
  }
}
