variable "region" {
  description = "The region the ephemeral stack runs in. Must be the foundation stack's region — every resource here attaches to that VPC."
  type        = string
  default     = "ap-southeast-2"
}

variable "image_tag" {
  description = "The ECR tag to deploy, which `scripts/push-image.sh` sets to the commit the image was built from. Deliberately has no default: a deploy names an image, and a defaulted tag would eventually name the wrong one."
  type        = string

  # Only that something was given. Whether the tag actually exists is a question about ECR rather
  # than about the string, so main.tf asks ECR — see the data source there.
  validation {
    condition     = length(trimspace(var.image_tag)) > 0
    error_message = "image_tag must be given. `scripts/push-image.sh` prints the tag it pushed."
  }
}
