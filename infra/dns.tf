locals {
  custom_domain = var.domain_name != "" ? 1 : 0
}

resource "aws_acm_certificate" "web" {
  count             = local.custom_domain
  provider          = aws.us_east_1
  domain_name       = var.domain_name
  validation_method = "DNS"
  tags              = var.tags

  # Renewing in place would briefly leave the distribution pointing at a certificate that
  # is being replaced.
  lifecycle {
    create_before_destroy = true
  }
}

# Indexed with count rather than for_each over domain_validation_options: that set is only
# known after apply, and using it for for_each keys is the standard way to get an
# "cannot be determined until apply" error on the very first plan. One domain, one record.
resource "aws_route53_record" "validation" {
  count = local.custom_domain

  zone_id = var.route53_zone_id
  name    = tolist(aws_acm_certificate.web[0].domain_validation_options)[0].resource_record_name
  type    = tolist(aws_acm_certificate.web[0].domain_validation_options)[0].resource_record_type
  records = [tolist(aws_acm_certificate.web[0].domain_validation_options)[0].resource_record_value]
  ttl     = 60

  # A re-issued certificate reuses the same record name, and without this the apply fails
  # on a record Terraform itself created.
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "web" {
  count                   = local.custom_domain
  provider                = aws.us_east_1
  certificate_arn         = aws_acm_certificate.web[0].arn
  validation_record_fqdns = [aws_route53_record.validation[0].fqdn]
}

# A and AAAA both, to match is_ipv6_enabled on the distribution -- an IPv6-only client
# gets no answer from an A record alone.
resource "aws_route53_record" "alias" {
  for_each = local.custom_domain == 1 ? toset(["A", "AAAA"]) : toset([])

  zone_id = var.route53_zone_id
  name    = var.domain_name
  type    = each.value

  alias {
    name                   = aws_cloudfront_distribution.web.domain_name
    zone_id                = aws_cloudfront_distribution.web.hosted_zone_id
    evaluate_target_health = false
  }
}
