# Hosting the browser head

S3 origin, CloudFront in front, nothing public but the distribution.

```
infra/
  versions.tf     providers; the us-east-1 alias exists only for ACM
  variables.tf    inputs
  s3.tf           origin bucket, locked to the distribution by SourceArn
  cloudfront.tf   OAC, cache policies, the distribution
  encoding.js     viewer-request rewrite to the pre-compressed sibling
  dns.tf          ACM + Route 53, only when domain_name is set
  waf.tf          IP allowlist, only when allowed_cidrs is set
  github.tf       OIDC deploy role, only when github_repository is set
  outputs.tf      names tools/deploy-web.sh reads back
```

## Who can see it

Set `demo_password` and the app is behind HTTP basic auth: stakeholders get the URL, a
username and a password, and it works from any network. Leave it empty and the app is
served to anyone who can reach it.

The check lives in `viewer-request.js.tftpl` alongside the encoding rewrite, because
CloudFront permits exactly **one** viewer-request function per cache behaviour — adding a
second function for auth was never an option. Authentication runs first. The credential is
injected by `templatefile()` at apply time rather than committed, though it is readable by
anyone with CloudFront read access on the account: this gates a demo, it is not a secret
store. There is one shared credential rather than per-person accounts, so rotating it
signs everyone out at once.

`allowed_cidrs` is the other lever — a WAF IP allowlist, default-deny, in `waf.tf`. The two
compose: set both and a viewer needs the right source address *and* the password. Note
that it is opt-in, so **emptying `allowed_cidrs` destroys the web ACL and stops
restricting by address** rather than blocking everyone.

Bear in mind CloudFront cannot be placed in a VPC — it is a public edge network by design.
An allowlist refuses unlisted sources but the traffic still crosses the internet, and it
only works at all if the people you want have static egress addresses. Split-tunnel VPNs
generally do not provide one.

## Why the requests get rewritten

`terraform apply` builds the infrastructure; `tools/deploy-web.sh` publishes and uploads.
The one part worth reading before changing anything is `encoding.js`.

A cold visit to this app is 41.2 MB of runtime. CloudFront's own `Compress` cannot reduce
it, for two separate reasons:

- it skips any object over 10,000,000 bytes, and `dotnet.native.wasm` is **30.5 MB** after
  AOT; and
- it only compresses a fixed list of content types, which does not include the
  `application/octet-stream` the ICU `.dat` files are served as.

Between them that is 33 of the 41 MB — so leaving compression to CloudFront gets almost
none of it. `dotnet publish` already writes a `.br` and a `.gz` next to every asset, so
`encoding.js` picks one on viewer-request and rewrites the URI to match:

| | raw | brotli |
|---|---|---|
| `dotnet.native.wasm` | 30.5 MB | 6.3 MB |
| whole bundle | 41.2 MB | **9.1 MB** |

Because the rewritten URI *is* the cache key, brotli, gzip and identity viewers each get
their own edge object without `Accept-Encoding` needing to be in the cache policy. A viewer
that sends no `Accept-Encoding` at all still gets the file exactly as published.

The matching half of this lives in `deploy-web.sh`, which uploads each `.br` under its own
key with the *original* file's `Content-Type` and `Content-Encoding: br`. Both sides have
to agree; changing one without the other is how you get a blank page.

## Caching

Most of `_framework/*` is content-addressed by the publish step
(`System.Private.CoreLib.0baromqu89.wasm`) — 129 of the 150 files — and those are cached
for a year and never revalidated.

The exceptions matter. `dotnet.js`, `avalonia.js`, `storage.js` and `sw.js` are published
under fixed names, and `dotnet.js` is the entry point `main.js` imports. Caching those for
a year would pin viewers to a loader that references framework files the next deploy has
already pruned, with no way to recover. So they, `index.html` and `main.js` are all
uploaded `no-cache` and revalidated.

CloudFront path patterns cannot express "has a content hash", so the split is made at
upload time by `deploy-web.sh` and the cache policy simply has `min_ttl = 0` so the
origin's `Cache-Control` wins. Those files are a few KB in total, so revalidating them
costs nothing — and it means a deploy takes effect without an invalidation.

## First run

```bash
cp terraform.tfvars.example terraform.tfvars   # edit region at least
terraform init
terraform apply
```

Then, from the repo root:

```bash
tools/deploy-web.sh
```

That publishes the browser head, uploads it with the right content types and cache headers,
prunes objects the publish no longer produces, and prints the URL.

State is local by default. To share it, create a bucket by hand and add a `backend "s3"`
block to `versions.tf` — it cannot be one this configuration manages, for the obvious
reason.

## Custom domain

Set both, in `terraform.tfvars`:

```hcl
domain_name     = "continuum.terrafa.io"
route53_zone_id = "Z0123456789ABCDEFGHIJ"
```

The zone must already exist and already be delegated — certificate validation writes a
CNAME into it and the apply will sit waiting until the NS records actually resolve. Leave
both unset to serve from the generated `*.cloudfront.net` name.

## Deploying from CI

`.github/workflows/deploy-web.yml` runs the same script on pushes to `main` that touch the
browser head or the UI project, and on `workflow_dispatch`.

It authenticates by OIDC, so there is no access key to leak or rotate. Set
`github_repository` and `terraform apply` creates the role:

```hcl
github_repository = "terrafa/continuum-frontend"
```

Then read the wiring straight out of the state and set it on the repository — one secret,
four variables:

```bash
terraform -chdir=infra output github_actions_config
```

The role trusts exactly one repository and one branch (`github_branch`, default `main`),
and carries only what the deploy actually does: `s3:ListBucket` on the bucket,
`s3:GetObject`/`PutObject`/`DeleteObject` on its contents, and
`cloudfront:CreateInvalidation` on the distribution. Nothing it holds can reach the rest of
the account, so it is safe to let the workflow run unattended.

An account can hold only one OIDC provider for GitHub. If something else already created
it, set `github_oidc_provider_arn` to reuse it rather than failing on
`EntityAlreadyExists`.

CI has no Terraform state, so the workflow passes `BUCKET` and `DISTRIBUTION` in as
environment variables and the script skips Terraform entirely. Locally, leave them unset
and it reads both from state — which is what stops a hand-run deploy landing in whichever
account the shell was pointed at.

To require a human before each deploy, add `environment: production` to the job and set
reviewers on that environment.

## If threading is ever enabled

`WasmEnableThreads` needs `SharedArrayBuffer`, which browsers only hand out to
cross-origin-isolated pages. That means adding `Cross-Origin-Opener-Policy: same-origin`
and `Cross-Origin-Embedder-Policy: require-corp` to the response headers policy in
`cloudfront.tf`. Do not add them pre-emptively — `require-corp` blocks every cross-origin
resource that does not opt in, so it breaks things on a page that has no need of it.
