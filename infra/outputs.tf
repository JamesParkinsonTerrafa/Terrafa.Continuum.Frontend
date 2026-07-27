# tools/deploy-web.sh reads bucket and distribution_id straight out of this state, so a
# deploy cannot be aimed at the wrong account by a stale environment variable.

output "bucket" {
  description = "Origin bucket the publish output is synced into."
  value       = aws_s3_bucket.web.id
}

output "distribution_id" {
  description = "Distribution to invalidate after a deploy."
  value       = aws_cloudfront_distribution.web.id
}

output "cloudfront_domain" {
  description = "Generated distribution domain, served whether or not a custom domain is set."
  value       = aws_cloudfront_distribution.web.domain_name
}

output "url" {
  description = "Where the app is actually reachable."
  value       = var.domain_name != "" ? "https://${var.domain_name}" : "https://${aws_cloudfront_distribution.web.domain_name}"
}

output "deploy_role_arn" {
  description = "Role for GitHub Actions to assume. Null unless github_repository is set."
  value       = one(aws_iam_role.github[*].arn)
}

# The four settings deploy-web.yml reads, in one place, so wiring up the repository is a
# copy rather than four lookups.
output "github_actions_config" {
  description = "Secret and variables to set on the repository."
  value = var.github_repository == "" ? null : {
    "secret AWS_DEPLOY_ROLE_ARN"   = one(aws_iam_role.github[*].arn)
    "variable AWS_REGION"          = var.region
    "variable WEB_BUCKET"          = aws_s3_bucket.web.id
    "variable WEB_DISTRIBUTION_ID" = aws_cloudfront_distribution.web.id
    "variable WEB_URL"             = var.domain_name != "" ? "https://${var.domain_name}" : "https://${aws_cloudfront_distribution.web.domain_name}"
  }
}
