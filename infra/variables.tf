variable "project" {
  description = "Name prefix for every resource, and the stem of the generated bucket name."
  type        = string
  default     = "terrafa-continuum"

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{1,38}$", var.project))
    error_message = "project must be lowercase alphanumeric and hyphens, so it can be used verbatim in an S3 bucket name."
  }
}

variable "region" {
  description = <<-EOT
    Region for the S3 bucket. Barely affects viewers -- every byte they see comes from a
    CloudFront edge -- so pick whichever is closest to whoever runs the deploy.
    The ACM certificate ignores this and is pinned to us-east-1; see versions.tf.
  EOT
  type        = string
  default     = "eu-west-2"
}

variable "bucket_name" {
  description = <<-EOT
    Origin bucket. Empty generates <project>-web-<account id>, which stays clear of the
    global S3 namespace without anyone having to invent a unique name.
  EOT
  type        = string
  default     = ""
}

variable "domain_name" {
  description = <<-EOT
    Custom domain to serve from, e.g. continuum.terrafa.io. Empty serves from the
    generated *.cloudfront.net name and skips ACM and Route 53 altogether.
  EOT
  type        = string
  default     = ""
}

variable "route53_zone_id" {
  description = <<-EOT
    Hosted zone that already owns domain_name. Required when domain_name is set: the
    certificate is DNS-validated and the alias records are written into this zone.
  EOT
  type        = string
  default     = ""

  validation {
    condition     = var.domain_name == "" || var.route53_zone_id != ""
    error_message = "route53_zone_id is required when domain_name is set, because the certificate is validated by DNS."
  }
}

variable "price_class" {
  description = <<-EOT
    PriceClass_100 is North America and Europe. PriceClass_200 adds most of Asia,
    PriceClass_All the rest, each at a higher per-GB rate.
  EOT
  type        = string
  default     = "PriceClass_100"

  validation {
    condition     = contains(["PriceClass_100", "PriceClass_200", "PriceClass_All"], var.price_class)
    error_message = "price_class must be PriceClass_100, PriceClass_200 or PriceClass_All."
  }
}

variable "demo_username" {
  description = "Username stakeholders sign in with. Only meaningful when demo_password is set."
  type        = string
  default     = "terrafa"
}

variable "demo_password" {
  description = <<-EOT
    Shared password for the demo. Empty disables the check and serves the app to anyone
    who can reach it.

    This gates a demo; it is not a secret store. The value is compiled into the CloudFront
    function and is readable by anyone with CloudFront read access on the account. Rotate
    it by changing this and re-applying -- everyone is signed out at once, since there is
    one credential rather than per-person accounts.
  EOT
  type        = string
  default     = ""
  sensitive   = true
}

variable "allowed_cidrs" {
  description = <<-EOT
    Source addresses allowed to reach the app. Empty serves it to the whole internet;
    anything listed switches the distribution to default-deny via WAF. See waf.tf.

    List both address families. The distribution is dual-stack and a browser with working
    IPv6 will use it, so a v4-only list blocks the very people it was written for.
  EOT
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for c in var.allowed_cidrs : can(regex("/[0-9]+$", c))])
    error_message = "Each entry needs a prefix length: WAF rejects a bare address, so write 203.0.113.4/32 rather than 203.0.113.4."
  }
}

variable "github_repository" {
  description = <<-EOT
    owner/name of the repository allowed to deploy, e.g. terrafa/continuum-frontend.
    Empty creates no IAM at all, which is the right setting if you only ever deploy by
    hand. See github.tf.
  EOT
  type        = string
  default     = ""

  validation {
    condition     = var.github_repository == "" || can(regex("^[^/]+/[^/]+$", var.github_repository))
    error_message = "github_repository must be owner/name, with no scheme and no trailing path."
  }
}

variable "github_branch" {
  description = "The one branch whose runs may assume the deploy role."
  type        = string
  default     = "main"
}

variable "github_oidc_provider_arn" {
  description = <<-EOT
    Existing GitHub OIDC provider to reuse. An account can hold only one per URL, so set
    this if something else in the account already created it; empty creates one.
  EOT
  type        = string
  default     = ""
}

variable "tags" {
  description = "Applied to every taggable resource."
  type        = map(string)
  default     = {}
}
