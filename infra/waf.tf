# IP allowlist in front of the distribution. Optional: leave allowed_cidrs empty and none
# of this is created, and the app is served to anyone.
#
# Worth being clear about what this is. CloudFront cannot be placed in a VPC -- it is a
# public edge network by design -- so this does not make the app private in a network
# sense. Every byte still crosses the public internet over TLS; WAF just refuses anyone
# whose source address is not listed. For an internal tool that is usually the right
# trade. If the requirement is that the traffic never be publicly routable at all, this is
# the wrong tool and the distribution has to go.

locals {
  waf = length(var.allowed_cidrs) > 0 ? 1 : 0

  # WAF keeps the two address families in separate IP sets, so the single input list is
  # split here rather than making the caller maintain two variables.
  cidrs_v4 = [for c in var.allowed_cidrs : c if !strcontains(c, ":")]
  cidrs_v6 = [for c in var.allowed_cidrs : c if strcontains(c, ":")]
}

# Both sets are created even when one is empty. An empty set simply never matches, and
# that costs nothing next to the alternative: the distribution is dual-stack and browsers
# prefer IPv6 when they have it, so an v4-only allowlist locks out exactly the people it
# was meant to admit, with a 403 that looks like a broken deploy.
resource "aws_wafv2_ip_set" "v4" {
  count = local.waf

  provider           = aws.us_east_1
  name               = "${var.project}-allowed-v4"
  description        = "Source addresses permitted to reach the browser head."
  scope              = "CLOUDFRONT"
  ip_address_version = "IPV4"
  addresses          = local.cidrs_v4
  tags               = var.tags
}

resource "aws_wafv2_ip_set" "v6" {
  count = local.waf

  provider           = aws.us_east_1
  name               = "${var.project}-allowed-v6"
  description        = "Source addresses permitted to reach the browser head."
  scope              = "CLOUDFRONT"
  ip_address_version = "IPV6"
  addresses          = local.cidrs_v6
  tags               = var.tags
}

# Scope and region are both fixed: a web ACL for CloudFront is a us-east-1 resource
# whatever region the bucket is in, which is the same constraint the ACM certificate has
# and the reason versions.tf carries the aliased provider.
#
# Adding this is a single apply. REMOVING it cannot be done by Terraform alone.
#
# Emptying allowed_cidrs makes Terraform go straight for DeleteWebACL while the
# distribution still references the ACL, and AWS refuses with WAFAssociatedItemException:
# the graph does not know the distribution has to be updated first. -target does not help
# either, because targeting the distribution pulls its dependencies -- including the very
# destroy you are trying to defer -- in with it. There is no -exclude before Terraform
# 1.16. Detach out of band, then let Terraform reconcile:
#
#   aws cloudfront get-distribution-config --id <id> > dist.json
#   # set DistributionConfig.WebACLId to "" and keep the ETag
#   aws cloudfront update-distribution --id <id> --if-match <etag> \
#     --distribution-config file://config.json
#   aws cloudfront wait distribution-deployed --id <id>
#   terraform apply     # the ACL is now unassociated and deletes cleanly
#
# The out-of-band edit causes no drift: Terraform already wants web_acl_id to be null, so
# the API change moves live state towards the config rather than away from it.
resource "aws_wafv2_web_acl" "web" {
  count = local.waf

  provider    = aws.us_east_1
  name        = "${var.project}-allowlist"
  description = "Default deny, with an allowance for known source addresses."
  scope       = "CLOUDFRONT"
  tags        = var.tags

  default_action {
    block {}
  }

  rule {
    name     = "allow-listed-ips"
    priority = 0

    action {
      allow {}
    }

    statement {
      or_statement {
        statement {
          ip_set_reference_statement {
            arn = aws_wafv2_ip_set.v4[0].arn
          }
        }
        statement {
          ip_set_reference_statement {
            arn = aws_wafv2_ip_set.v6[0].arn
          }
        }
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "${var.project}-allowed"
      # Keeps the last few hours of blocked requests visible in the console, with their
      # source addresses. This is what turns "why can I not load the site" into a
      # two-minute answer instead of a guess.
      sampled_requests_enabled = true
    }
  }

  visibility_config {
    cloudwatch_metrics_enabled = true
    metric_name                = "${var.project}-web"
    sampled_requests_enabled   = true
  }
}
