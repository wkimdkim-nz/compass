terraform {
  required_version = ">= 1.11.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  # A second state file in the same bucket, under its own key. Separate state is what makes this
  # stack destroyable on its own: `terraform -chdir=infra/app destroy` can never reach a foundation
  # resource, because none of them are in this state file to reach.
  #
  # The bucket is the one `scripts/bootstrap-state-bucket.sh` created for the foundation stack.
  # Locking is S3-native, as it is there.
  backend "s3" {
    bucket       = "compass-tfstate-625495455169"
    key          = "app/terraform.tfstate"
    region       = "ap-southeast-2"
    encrypt      = true
    use_lockfile = true
  }
}
