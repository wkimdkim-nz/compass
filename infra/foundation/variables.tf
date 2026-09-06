variable "region" {
  description = "The region the foundation stack runs in. CloudFront's certificate is the one exception; see providers.tf."
  type        = string
  default     = "ap-southeast-2"
}

variable "domain_name" {
  description = "The apex the SPA is served from. `.dev` is HSTS-preloaded as a whole TLD, so HTTPS is browser-enforced and the session cookie's Secure flag is unbypassable."
  type        = string
  default     = "financial-compass.dev"
}

variable "hosted_zone_id" {
  description = "The public hosted zone created when the domain was registered (#9). Adopted, never recreated: the registrar's delegation points at this zone's name servers, and a replacement zone would get different ones."
  type        = string
  default     = "Z1048295E8RMMBNI5Z3A"
}
