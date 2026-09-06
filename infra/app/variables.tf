variable "region" {
  description = "The region the ephemeral stack runs in. Must be the foundation stack's region — every resource here attaches to that VPC."
  type        = string
  default     = "ap-southeast-2"
}

variable "image_tag" {
  description = "The ECR tag to deploy, which `scripts/push-image.sh` sets to the commit the image was built from. Deliberately has no default: a deploy names an image, and a defaulted tag would eventually name the wrong one."
  type        = string

  validation {
    condition     = length(trimspace(var.image_tag)) > 0
    error_message = "image_tag must name a tag that exists in the compass-api repository (scripts/push-image.sh prints it)."
  }
}

# Stated rather than defaulted, and the reason is money. AWS documents the Fargate default two
# ways — 256/512 in the API reference, 1024/2048 in the developer guide — a fourfold swing between
# roughly $10.80 and $43.22 a month (ADR 0009). A documented default must not be allowed to decide
# the bill, so both are named here and passed explicitly to the task definition.
variable "task_cpu" {
  description = "Fargate CPU units for the task. 256 = 0.25 vCPU."
  type        = number
  default     = 256
}

variable "task_memory" {
  description = "Fargate memory for the task, in MiB. 512 is the only value 256 CPU units allow alongside itself."
  type        = number
  default     = 512
}
