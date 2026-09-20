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

variable "edgar_contact_email" {
  description = "The contact address the API puts in its User-Agent when calling SEC EDGAR, which refuses requests without one. Deliberately has no default: ADR 0010 keeps the address out of source, so it arrives with the apply — `-var` or `TF_VAR_edgar_contact_email`."
  type        = string

  # Only that it looks like an address. A task handed a blank one refuses to start (ADR 0010), and
  # the plan is a cheaper place to find that out than a service that never goes steady.
  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+$", var.edgar_contact_email))
    error_message = "edgar_contact_email must be an email address. Pass it with -var or export TF_VAR_edgar_contact_email."
  }
}
