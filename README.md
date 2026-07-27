# Terrafa Continuum — Frontend

An Avalonia app with two heads over one UI project:

| | project | ships as |
|---|---|---|
| desktop | `src/Terrafa.Continuum.Frontend` | installers on a GitHub release |
| browser | `src/Terrafa.Continuum.Frontend.Browser` | wasm bundle on S3 behind CloudFront |
| shared UI | `src/Terrafa.Continuum.Frontend.Ui` | referenced by both |

The two deploy independently and on different triggers. Neither waits on the other, and a
failure in one does not hold up the other.

## Prerequisites

- **.NET 10 SDK** — both heads target `net10.0`.
- **Python 3** — for `tools/check-glyphs.py`, which both workflows run as a gate.
- For the browser head: **`dotnet workload install wasm-tools`**. The project sets
  `RunAOTCompilation`, and without the workload `publish` silently falls back to the
  interpreter instead of failing, so the first sign of trouble is a bundle that stalls on
  load.
- For deploying from your own machine: **AWS CLI** with credentials, and **Terraform**.

## Before deploying

```bash
python3 tools/check-glyphs.py
```

Both workflows run this and fail on a non-zero exit. It catches two things: a character no
embedded font covers (a browser has no system font stack behind the app, so it ships as a
tofu box), and a character the font *claims* to cover but draws with the wrong glyph.

To look at the views without launching anything, render them headlessly:

```bash
dotnet run --project src/Terrafa.Continuum.Frontend/Terrafa.Continuum.Frontend.csproj -- --snapshot publish/snapshots
```

That writes a PNG per view — light, dark, hints-off and a few interaction states — under
`publish/`, which is gitignored.

## Deploying the browser head

### From CI — the normal path

`.github/workflows/deploy-web.yml` runs on any push to `main` touching the browser head,
the UI project, or the deploy script, and on **Actions ▸ Deploy web ▸ Run workflow**.

It authenticates by OIDC, so there is no access key to rotate. It has no Terraform state,
so it takes the bucket and distribution from repository variables and never invokes
Terraform at all.

### From your machine

```bash
tools/deploy-web.sh
```

That publishes the browser head, uploads it with the right content types and cache
headers, prunes objects the publish no longer produces, invalidates the entry points, and
prints the URL. Pass `--skip-publish` to re-upload an existing `publish/web` without
rebuilding.

It reads the bucket and distribution out of local Terraform state rather than the
environment, which is what stops a hand-run deploy landing in whichever account your shell
was pointed at. State is local and gitignored, so **only a machine holding
`infra/terraform.tfstate` can deploy this way** — there is no shared backend. Setting
`BUCKET` and `DISTRIBUTION` skips Terraform, which is the door CI goes through.

### Standing the infrastructure up the first time

```bash
cp infra/terraform.tfvars.example infra/terraform.tfvars   # edit region at least
terraform -chdir=infra init
terraform -chdir=infra apply
tools/deploy-web.sh
```

[`infra/README.md`](infra/README.md) is the reference for what gets built and why — the
encoding rewrite that turns a 41 MB cold load into 9 MB, the caching split, custom
domains, and the threading headers. Read it before changing anything under `infra/`.

### Who can reach it

Access is HTTP basic auth, checked by the CloudFront viewer-request function. The
credential is `demo_password` in `infra/terraform.tfvars`, which is gitignored — it is
injected at apply time, not committed. There is one shared credential rather than
per-person accounts, so rotating it signs every stakeholder out at once.

This repository is public, so the distribution URL is deliberately not written down here —
one shared password is the only thing in front of it. `tools/deploy-web.sh` prints it at
the end of a deploy, and it is in state:

```bash
terraform -chdir=infra output -raw url
```

### Wiring CI once

Set `github_repository` in `terraform.tfvars`, apply, then read the wiring back out:

```bash
terraform -chdir=infra output github_actions_config
```

That prints one secret (`AWS_DEPLOY_ROLE_ARN`) and four variables (`AWS_REGION`,
`WEB_BUCKET`, `WEB_DISTRIBUTION_ID`, `WEB_URL`) to set on the repository. The role trusts
exactly one repository and one branch and can reach nothing else in the account.

## Releasing the desktop app

`.github/workflows/release.yml` runs on **every push to `main`**, and on **Actions ▸
Release ▸ Run workflow**. There is nothing to run by hand.

It resolves the next version by bumping the patch on the highest existing `v*.*.*` tag,
builds `win-x64`, `osx-x64`, `osx-arm64` and `linux-x64` as self-contained single files,
packages each, and publishes a GitHub release with `SHA256SUMS` and the install scripts
attached:

- macOS — `.app` in a drag-to-Applications `.dmg`, plus a zip
- Windows — Inno Setup `setup.exe`, plus a zip
- Linux — `.deb`, plus a zip

Version numbers are derived, and only ever move the patch. To start a new minor or major
line, create that tag yourself and the next run continues from it.

Nothing is signed or notarised, so the downloadable installers need right-click ▸ Open on
macOS and **More info ▸ Run anyway** on Windows. The scripted install avoids both, because
neither `curl` nor `Invoke-WebRequest` marks the download the way a browser does:

```bash
curl -fsSL https://raw.githubusercontent.com/JamesParkinsonTerrafa/Terrafa.Continuum.Frontend/main/install.sh | bash
```

```powershell
irm https://raw.githubusercontent.com/JamesParkinsonTerrafa/Terrafa.Continuum.Frontend/main/install.ps1 | iex
```

Both honour `TERRAFA_INSTALL_DIR`.

## Checking a deploy landed

The web entry points (`index.html`, `main.js`, `dotnet.js` and friends) are uploaded
`no-cache` and revalidate at the edge, so a deploy takes effect on a reload without waiting
on the invalidation. The workflow summary prints the URL it deployed to.

## Rolling back

There is no versioned artifact in the bucket — the deploy prunes whatever the current
publish does not produce — so rolling the web head back means checking out the last good
commit and deploying it again:

```bash
git checkout <good-sha>
tools/deploy-web.sh
```

Desktop releases are immutable once tagged, so rolling back there is a matter of
installing an earlier release's assets.
