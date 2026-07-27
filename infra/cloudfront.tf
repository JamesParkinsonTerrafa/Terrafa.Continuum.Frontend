resource "aws_cloudfront_origin_access_control" "web" {
  name                              = "${var.project}-web"
  description                       = "Signs CloudFront's origin reads against ${local.bucket_name}."
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# Basic auth plus the encoding rewrite, in one function because CloudFront allows only one
# viewer-request function per behaviour. Rendered rather than read verbatim so the
# credential is injected at apply time and never lives in the repository.
resource "aws_cloudfront_function" "encoding" {
  name    = "${var.project}-encoding"
  runtime = "cloudfront-js-2.0"
  comment = "Gates on basic auth, then rewrites to the published .br/.gz sibling."
  publish = true

  code = templatefile("${path.module}/viewer-request.js.tftpl", {
    expected_auth = var.demo_password == "" ? "" : "Basic ${base64encode("${var.demo_username}:${var.demo_password}")}"
  })
}

# Most of _framework/ carries a content hash in the filename
# (System.Private.CoreLib.0baromqu89.wasm), so a changed file is a new URL and the old URL
# can be held forever. That is what makes a repeat visit free rather than 41 MB.
#
# But not all of it: dotnet.js, avalonia.js, storage.js and sw.js are published under fixed
# names, and dotnet.js is the entry point main.js imports. Caching those for a year would
# pin viewers to a loader pointing at framework files the next deploy has already pruned,
# with no way to recover. So min_ttl is 0 and the deploy script sets Cache-Control per file
# -- immutable on the hashed ones, no-cache on the four that keep their names. Terraform
# cannot make that split itself; CloudFront path patterns have no way to say "has a hash".
resource "aws_cloudfront_cache_policy" "immutable" {
  name        = "${var.project}-immutable"
  comment     = "Framework assets. Origin Cache-Control decides, per file."
  min_ttl     = 0
  default_ttl = 31536000
  max_ttl     = 31536000

  parameters_in_cache_key_and_forwarded_to_origin {
    # Left off deliberately. encoding.js has already folded the viewer's encoding into the
    # URI, so adding Accept-Encoding here would only split each object into three
    # identical cache entries.
    enable_accept_encoding_brotli = false
    enable_accept_encoding_gzip   = false

    cookies_config {
      cookie_behavior = "none"
    }
    headers_config {
      header_behavior = "none"
    }
    query_strings_config {
      query_string_behavior = "none"
    }
  }
}

# index.html and main.js are the two files publish does not fingerprint, so they are the
# two that can go stale. A cached index.html points at _framework files the next deploy has
# already replaced, and the app fails to boot -- hence no edge caching at all. They are
# 1.5 KB and 1 KB, so the origin fetch costs nothing and deploys need no invalidation.
resource "aws_cloudfront_cache_policy" "revalidate" {
  name        = "${var.project}-revalidate"
  comment     = "Unfingerprinted entry points. Always revalidated."
  min_ttl     = 0
  default_ttl = 0
  max_ttl     = 0

  parameters_in_cache_key_and_forwarded_to_origin {
    enable_accept_encoding_brotli = false
    enable_accept_encoding_gzip   = false

    cookies_config {
      cookie_behavior = "none"
    }
    headers_config {
      header_behavior = "none"
    }
    query_strings_config {
      query_string_behavior = "none"
    }
  }
}

resource "aws_cloudfront_response_headers_policy" "web" {
  name    = "${var.project}-web"
  comment = "Baseline security headers for a static origin."

  security_headers_config {
    strict_transport_security {
      access_control_max_age_sec = 31536000
      include_subdomains         = true
      # Left off: preloading is a one-way door that binds the apex and every subdomain,
      # and it is not this distribution's call to make on behalf of the whole zone.
      preload  = false
      override = true
    }

    content_type_options {
      override = true
    }

    frame_options {
      frame_option = "SAMEORIGIN"
      override     = true
    }

    referrer_policy {
      referrer_policy = "strict-origin-when-cross-origin"
      override        = true
    }
  }
}

resource "aws_cloudfront_distribution" "web" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "${var.project} browser head"
  default_root_object = "index.html"
  price_class         = var.price_class
  aliases             = var.domain_name != "" ? [var.domain_name] : []
  http_version        = "http2and3"
  tags                = var.tags

  # Null when allowed_cidrs is empty, which serves the app to everyone. Despite the field
  # name this wants the web ACL's ARN, not its id -- an id here is accepted at plan time
  # and fails on apply.
  web_acl_id = one(aws_wafv2_web_acl.web[*].arn)

  origin {
    origin_id                = "s3"
    domain_name              = aws_s3_bucket.web.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.web.id
  }

  default_cache_behavior {
    target_origin_id       = "s3"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]

    # Off on purpose. Everything is served pre-compressed from S3 with a Content-Encoding
    # already set, which CloudFront will not touch, and the identity fallback is only
    # reached by viewers that asked for no encoding.
    compress = false

    cache_policy_id            = aws_cloudfront_cache_policy.revalidate.id
    response_headers_policy_id = aws_cloudfront_response_headers_policy.web.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.encoding.arn
    }
  }

  ordered_cache_behavior {
    path_pattern           = "/_framework/*"
    target_origin_id       = "s3"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    compress               = false

    cache_policy_id            = aws_cloudfront_cache_policy.immutable.id
    response_headers_policy_id = aws_cloudfront_response_headers_policy.web.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.encoding.arn
    }
  }

  # S3 answers a missing key with 403 rather than 404, because the bucket policy grants
  # GetObject but not ListBucket. Both are mapped so that a mistyped path lands on the app.
  #
  # The cost of this: a half-finished deploy serves index.html in place of a missing
  # _framework file, and the browser reports a wasm magic-word mismatch instead of a plain
  # 404. Delete both blocks if you would rather read the real status code.
  custom_error_response {
    error_code            = 403
    response_code         = 200
    response_page_path    = "/index.html"
    error_caching_min_ttl = 0
  }

  custom_error_response {
    error_code            = 404
    response_code         = 200
    response_page_path    = "/index.html"
    error_caching_min_ttl = 0
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = var.domain_name == ""

    # All three have to stay null on the default certificate; CloudFront rejects the pair.
    acm_certificate_arn      = var.domain_name != "" ? aws_acm_certificate_validation.web[0].certificate_arn : null
    ssl_support_method       = var.domain_name != "" ? "sni-only" : null
    minimum_protocol_version = var.domain_name != "" ? "TLSv1.2_2021" : null
  }
}
