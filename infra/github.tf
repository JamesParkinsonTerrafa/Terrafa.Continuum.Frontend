# The role .github/workflows/deploy-web.yml assumes. Entirely optional: leave
# github_repository empty and none of this is created.
#
# OIDC rather than an access key pair, so there is no long-lived credential to leak or
# rotate. GitHub mints a short-lived token per run, AWS trades it for session credentials,
# and the trust policy below decides whose token counts.

locals {
  github_ci   = var.github_repository != "" ? 1 : 0
  create_oidc = var.github_repository != "" && var.github_oidc_provider_arn == "" ? 1 : 0

  # github_subjects wins when set, because a repository issuing immutable subject claims
  # cannot be described by owner/name at all -- see the variable for what that looks like.
  github_subjects = length(var.github_subjects) > 0 ? var.github_subjects : [
    "repo:${var.github_repository}:ref:refs/heads/${var.github_branch}",
  ]

  # one() yields null for the not-created case, which coalesce then skips.
  github_oidc_arn = coalesce(
    var.github_oidc_provider_arn,
    one(aws_iam_openid_connect_provider.github[*].arn),
    "unused",
  )
}

# An account can only hold one provider per URL, so this is skipped when
# github_oidc_provider_arn names an existing one -- otherwise a second stack in the same
# account fails on EntityAlreadyExists.
resource "aws_iam_openid_connect_provider" "github" {
  count = local.create_oidc

  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]

  # AWS validates GitHub's certificate chain against its own trusted CAs and no longer
  # relies on this value, but the API still refuses to create a provider without one.
  thumbprint_list = ["6938fd4d98bab03faadb97b34396831e3780aea1"]

  tags = var.tags
}

data "aws_iam_policy_document" "github_assume" {
  count = local.github_ci

  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [local.github_oidc_arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # The load-bearing one. Without a sub condition this trusts any GitHub Actions run on
    # any repository in the world -- the aud check alone does not narrow it at all.
    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values   = local.github_subjects
    }
  }
}

# Exactly what tools/deploy-web.sh does and nothing more: list, upload, prune, invalidate.
# Note there is no PutObjectAcl -- the bucket is BucketOwnerEnforced, so object ACLs do
# not exist to be set.
data "aws_iam_policy_document" "github_deploy" {
  count = local.github_ci

  statement {
    sid       = "ListForSyncAndPrune"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.web.arn]
  }

  statement {
    sid       = "WriteObjects"
    actions   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.web.arn}/*"]
  }

  statement {
    sid       = "InvalidateEntryPoints"
    actions   = ["cloudfront:CreateInvalidation"]
    resources = [aws_cloudfront_distribution.web.arn]
  }
}

resource "aws_iam_role" "github" {
  count = local.github_ci

  name               = "${var.project}-deploy-web"
  description        = "Assumed by GitHub Actions to deploy the browser head."
  assume_role_policy = data.aws_iam_policy_document.github_assume[0].json
  tags               = var.tags
}

resource "aws_iam_role_policy" "github" {
  count = local.github_ci

  name   = "deploy"
  role   = aws_iam_role.github[0].id
  policy = data.aws_iam_policy_document.github_deploy[0].json
}
