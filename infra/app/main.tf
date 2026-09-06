# What this stack starts from, and the two very different ways it learns it.
#
# **The network comes through remote state.** The VPC, its public subnets, the security-group chain
# and the registry are all facts about a stack that is never destroyed, and the foundation stack
# publishes them as outputs for exactly this reason (see its outputs.tf). Reading them here is a
# read of a persistent thing by an ephemeral one, which is the safe direction: nothing in this
# stack can change them, and a `terraform destroy` here cannot reach them, because they are not in
# this state file.
#
# **The origin header's value does not.** It is fetched from Secrets Manager at plan time, by the
# name the foundation stack publishes. That is the one deliberate exception and it mirrors the DNS
# decoupling: CloudFront holds the same value in its own configuration, and the two stacks agree on
# it through a *name* rather than through a reference either of them could make the other's
# configuration change. The value is not in the foundation's outputs at all.
#
# ⚠️ It is, however, in *this* stack's state, which is inherent to reading it: the listener rule
# has to compare against a literal. That is acceptable because it is an origin lock and not a
# credential — anyone who can call `cloudfront get-distribution-config` can already read it — and
# because this state is torn down with the stack it describes.

data "terraform_remote_state" "foundation" {
  backend = "s3"

  config = {
    bucket = "compass-tfstate-625495455169"
    key    = "foundation/terraform.tfstate"
    region = "ap-southeast-2"
  }
}

data "aws_secretsmanager_secret_version" "origin_header" {
  secret_id = local.foundation.origin_secret_name
}

locals {
  foundation = data.terraform_remote_state.foundation.outputs

  # {header_name, header_value}. The name travels with the value in one document, so there is no
  # second constant for the two stacks to keep in step.
  origin_header = jsondecode(data.aws_secretsmanager_secret_version.origin_header.secret_string)

  # The port the image already defaults to, carried from the foundation stack rather than restated,
  # because the target group depends on it silently: wrong, and every health check fails with
  # nothing in any log to say why.
  container_port = local.foundation.container_port

  # An immutable tag in a repository whose tags are immutable, so this string is evidence of what
  # is running rather than a pointer someone may have moved.
  image = "${local.foundation.ecr_repository_url}:${var.image_tag}"

  # The one container in the one task definition. Named here because three separate resources have
  # to agree on it — the task definition, the service's load balancer block, and the log stream
  # prefix — and a mismatch between the first two is rejected only when the service is created.
  container_name = "api"
}
