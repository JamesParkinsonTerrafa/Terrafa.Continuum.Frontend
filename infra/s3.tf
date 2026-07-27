data "aws_caller_identity" "current" {}

locals {
  bucket_name = var.bucket_name != "" ? var.bucket_name : "${var.project}-web-${data.aws_caller_identity.current.account_id}"
}

resource "aws_s3_bucket" "web" {
  bucket = local.bucket_name
  tags   = var.tags
}

# ACLs off entirely. Nothing writes here except the deploy credentials, and CloudFront
# reads through the bucket policy below, so there is no case left for an object ACL.
resource "aws_s3_bucket_ownership_controls" "web" {
  bucket = aws_s3_bucket.web.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_public_access_block" "web" {
  bucket = aws_s3_bucket.web.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "web" {
  bucket = aws_s3_bucket.web.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    # Cuts the per-object KMS-style request overhead. Free with SSE-S3, so no reason not to.
    bucket_key_enabled = true
  }
}

# Nothing here is precious -- every object is reproducible from a `dotnet publish` -- but
# versioning makes a bad deploy recoverable without a rebuild, which is worth the pennies.
resource "aws_s3_bucket_versioning" "web" {
  bucket = aws_s3_bucket.web.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "web" {
  bucket = aws_s3_bucket.web.id

  # Versioning without this grows the bucket by a full 60 MB runtime on every deploy,
  # forever, and nobody notices until the bill does.
  rule {
    id     = "expire-noncurrent"
    status = "Enabled"

    filter {}

    noncurrent_version_expiration {
      noncurrent_days = 30
    }
  }

  rule {
    id     = "abort-incomplete-uploads"
    status = "Enabled"

    filter {}

    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }

  depends_on = [aws_s3_bucket_versioning.web]
}

data "aws_iam_policy_document" "web" {
  statement {
    sid       = "AllowCloudFrontRead"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.web.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    # Without SourceArn this grants read to the CloudFront service as a whole, which means
    # any distribution in any AWS account could be pointed at this bucket.
    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.web.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "web" {
  bucket = aws_s3_bucket.web.id
  policy = data.aws_iam_policy_document.web.json

  # The public access block has to land first, or the policy write races it and can be
  # rejected as public.
  depends_on = [aws_s3_bucket_public_access_block.web]
}
