terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.60"
    }
  }
}

# Everything except the certificate lives in var.region.
provider "aws" {
  region = var.region
}

# CloudFront only reads ACM certificates from us-east-1, whatever region the bucket is in.
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
}
