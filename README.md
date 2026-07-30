# Terrafa Continuum — Frontend

## Install

**macOS / Linux**

1. Open **Terminal** (on a Mac: press ⌘ Space, type `Terminal`, press Return).
2. Copy the command below, paste it into the Terminal window and press Return:

```bash
curl -fsSL https://raw.githubusercontent.com/JamesParkinsonTerrafa/Terrafa.Continuum.Frontend/main/install.sh | bash
```

3. Wait for the message **Continuum installed**. On a Mac, click
   **Terrafa Continuum** in `/Applications` to run the app.

**Windows**

1. Open **PowerShell** (press the Windows key, type `PowerShell`, press Enter).
2. Copy the command below, paste it into the PowerShell window and press Enter:

```powershell
irm https://raw.githubusercontent.com/JamesParkinsonTerrafa/Terrafa.Continuum.Frontend/main/install.ps1 | iex
```

3. Wait for the message **Continuum installed**. Click **Terrafa Continuum** in
   the Start Menu to run the app.

## Architecture

An Avalonia app with two heads over one UI project:

| | project | ships as |
|---|---|---|
| desktop | `src/Terrafa.Continuum.Frontend` | installers on a GitHub release |
| browser | `src/Terrafa.Continuum.Frontend.Browser` | wasm bundle on S3 behind CloudFront |
| shared UI | `src/Terrafa.Continuum.Frontend.Ui` | referenced by both |

The two deploy independently and on different triggers. Neither waits on the other, and a
failure in one does not hold up the other.

## Zero to running

Every step, in order, for an account and a checkout with none of this done. Steps 1–3 are the
frontend's own infrastructure; steps 4–6 are the backend it reads from, and live in
[`Terrafa.Continuum.Core.DataFeed`](https://github.com/JamesParkinsonTerrafa/Terrafa.Continuum.Core.DataFeed).
Each step says how to tell it worked, because most of these fail silently.

**1. Stand up the hosting.** Creates the bucket, the distribution, the certificate and the
GitHub deploy role. `infra/terraform.tfvars` is gitignored, so copy the example and set `region`
at minimum:

```bash
cp infra/terraform.tfvars.example infra/terraform.tfvars
```

Set `github_repository` to `owner/name` to get the CI deploy role. Two settings alongside it are
easy to miss, and each fails in a way that does not name itself:

- `github_oidc_provider_arn` — an account can hold only one provider per URL, so if anything else
  in the account already made one, leaving this empty fails the apply with `EntityAlreadyExists`.
- `github_subjects` — **GitHub issues immutable subject claims for these repositories**, meaning
  the owner and repository carry their numeric IDs
  (`repo:owner@306613510/name@1309267104:ref:refs/heads/main`). The default `repo:owner/name:...`
  subject then never matches and every run dies at the assume step with `Not authorized to perform
  sts:AssumeRoleWithWebIdentity` — which names no claim and reads like a missing permission rather
  than a mismatched string. Read the real prefix with:

```bash
gh api repos/OWNER/NAME/actions/oidc/customization/sub
```

```bash
terraform -chdir=infra init && terraform -chdir=infra apply
```

**2. Publish the CI values.** The stack prints everything the deploy workflow needs, already
labelled with whether each is a variable or a secret:

```bash
terraform -chdir=infra output github_actions_config
```

Each key names its own kind. `AWS_REGION`, `WEB_BUCKET`, `WEB_DISTRIBUTION_ID` and `WEB_URL` are
Actions **variables**; `AWS_DEPLOY_ROLE_ARN` is a **secret**. The workflow reads them by kind, so
setting a variable as a secret or the reverse leaves it reading empty — which surfaces one step
later as `Input required and not supplied: aws-region`. Setting them straight from the output
avoids getting that wrong:

```bash
terraform -chdir=infra output -json github_actions_config | jq -r 'to_entries[] | select(.key | startswith("variable ")) | [(.key | sub("^variable ";"")), .value] | @tsv' | while IFS=$'\t' read -r name value; do gh variable set "$name" --body "$value"; done
```

```bash
gh secret set AWS_DEPLOY_ROLE_ARN --body "$(terraform -chdir=infra output -raw deploy_role_arn)"
```

**3. Deploy the browser head.** Either push to `main`, or run it by hand:

```bash
gh workflow run "Deploy web" && gh run watch
```

**4. Deploy the data feed.** Follow that repo's own *Deploying from nothing* — bootstrap stack,
GitHub `production` environment, five Actions variables, then `gh workflow run deploy`. It applies
Lambda, API Gateway and Cognito. Its main stack is applied **by the pipeline**, not from your
machine: a developer IAM user typically lacks `apigateway:GET` and `logs:ListTagsForResource`, so a
local `terraform plan` fails on refresh before it reaches an apply.

**5. Create a user.** Self sign-up is off. Both commands are needed — see
[Creating a user](#creating-a-user) for why, and for the paste hazard that silently leaves the
account unusable.

**6. Point the app at it.** Region, user pool, app client and the API address are compiled into
[`AuthOptions.cs`](src/Terrafa.Continuum.Frontend.Ui/Services/AuthOptions.cs) and
[`DataFeedOptions.cs`](src/Terrafa.Continuum.Frontend.Ui/Services/DataFeedOptions.cs) — see
[The deployed values](#the-deployed-values). Then confirm the whole chain end to end, which needs
no credentials:

```bash
dotnet test
```

Two things are still outstanding on a fresh stand-up and neither is done by any command above:
`cors_allow_origins` in the DataFeed stack must list the CloudFront origin, or the browser head
signs in and then fails every data call on preflight; and `api_required_scopes` must be `[]`,
because no `InitiateAuth` token can carry a custom scope. Both are covered in
[Still outstanding on the AWS side](#still-outstanding-on-the-aws-side).

## Prerequisites

- **.NET 10 SDK** — both heads target `net10.0`.
- **Python 3** — for `tools/check-glyphs.py`, which both workflows run as a gate.
- For the browser head: **`dotnet workload install wasm-tools`**. The project sets
  `RunAOTCompilation`, and without the workload `publish` silently falls back to the
  interpreter instead of failing, so the first sign of trouble is a bundle that stalls on
  load.
- For deploying from your own machine: **AWS CLI** with credentials, and **Terraform**.

## The data feed

The CATALOGUE and SUBTREE PREVIEW on the DATA SOURCES screen read from the
`Terrafa.Continuum.Core.DataFeed` service, which serves the datasets in the configured Athena
databases over `GET /api/datasets`, `.../{database}/{table}/schema` and `.../data`. Nothing else does yet — every
other screen still renders `StaticDataFeed`, because nothing in the Athena catalog can supply
the positions, calibration or event log those screens show.

Nobody sees live data until they sign in. **CONNECT REAL DATA** on the DATA SOURCES screen offers
two routes: sign in with credentials you issued, or request a demo account — which opens the
sender's mail app addressed to `info@terrafa.uk` with their name, email and company filled in.
Until someone signs in the app shows the built-in demo catalogue, and the status bar says
`CATALOGUE READING DEMO DATA` so nobody mistakes one for the other.

Signing in or out empties the workspace. A subtree mounted from the demo catalogue means nothing
against a real one, so carrying mounts across the switch would leave the tree and network screens
drawing leaves whose dataset is no longer listed.

### The deployed values

Everything the app needs to find the backend is compiled in. None of it is a secret — a browser
app cannot authenticate without knowing which pool to authenticate against, and all of it travels
in the clear on the first request any signed-in browser makes.

| Value | Lives in | Current |
| --- | --- | --- |
| DataFeed base address | [`Services/DataFeedOptions.cs`](src/Terrafa.Continuum.Frontend.Ui/Services/DataFeedOptions.cs) | `https://0ncy4qt6v1.execute-api.eu-north-1.amazonaws.com` |
| Cognito region | [`Services/AuthOptions.cs`](src/Terrafa.Continuum.Frontend.Ui/Services/AuthOptions.cs) | `eu-north-1` |
| App client id | same | `2lroc37l2gjoi6nnvbitpm4m57` |
| User pool id | same | `eu-north-1_dgRWlrr7C` |

They are constants rather than configuration because **the browser head has no environment**:
`Environment.GetEnvironmentVariable` returns null under wasm whatever the deploy sets, so an
environment-only default left the web build permanently unable to sign in. The desktop head does
have an environment, and these still override there:

```bash
TERRAFA_DATAFEED_URL=http://127.0.0.1:5205 dotnet run --project src/Terrafa.Continuum.Frontend/Terrafa.Continuum.Frontend.csproj
```

`TERRAFA_COGNITO_REGION`, `TERRAFA_COGNITO_CLIENT_ID` and `TERRAFA_COGNITO_USER_POOL_ID` work the
same way. To re-read them all from the deployed stack:

```bash
terraform -chdir=terraform output
```

### Sign-in

Accounts are ones you create — there is no self-registration. Sign-in uses **`USER_SRP_AUTH`**,
which proves knowledge of the password without ever sending it; the app takes the access token
back and sends it as `Authorization: Bearer` on every call to the DataFeed service. The token is
held in memory only, so closing the app signs out; it is renewed automatically about two minutes
before it expires, and a revoked refresh token drops the app back to demo data rather than failing
silently.

SRP rather than `USER_PASSWORD_AUTH` because that is the only user-facing flow the pool's app
client allows, deliberately. The arithmetic comes from `Amazon.Extensions.CognitoAuthentication`
rather than being hand-rolled — trimmed, it costs about 190 KB brotli in the wasm bundle.

**Two shims in [`Services/CognitoAuthenticator.cs`](src/Terrafa.Continuum.Frontend.Ui/Services/CognitoAuthenticator.cs)
exist only for the browser head.** AWS does not support browser-wasm for AWSSDK.Core, and it fails
there in two ways that both look like unrelated crashes: it builds a `SocketsHttpHandler` that wasm
has no implementation of, and it unmarshals responses with a synchronous `Stream.Read` that the
browser's async-only response stream refuses. Both are inert on the desktop head, and removing
either breaks sign-in **on the web build only**.

#### Creating a user

Self sign-up is off. Both commands are needed: `admin-create-user` issues a *temporary* password,
which leaves the account in `FORCE_CHANGE_PASSWORD` — Cognito then answers sign-in with a
`NEW_PASSWORD_REQUIRED` challenge that this app deliberately cannot complete. The second command
is what makes the password you hand out actually work, and it is once per user, not a recurring
prompt anyone sees.

```bash
POOL=eu-north-1_dgRWlrr7C
```

```bash
aws cognito-idp admin-create-user --user-pool-id "$POOL" --username you@example.com --user-attributes Name=email,Value=you@example.com Name=email_verified,Value=true --message-action SUPPRESS --region eu-north-1
```

```bash
aws cognito-idp admin-set-user-password --user-pool-id "$POOL" --username you@example.com --password 'YourPassword1!' --permanent --region eu-north-1
```

Plain ASCII quotes and a double hyphen on `--permanent` — a smart quote or an en dash pasted from
a document makes the CLI reject the command, and the account silently stays in
`FORCE_CHANGE_PASSWORD`. Check it took:

```bash
aws cognito-idp list-users --user-pool-id eu-north-1_dgRWlrr7C --query 'Users[].{u:Username,status:UserStatus}' --output table --region eu-north-1
```

`CONFIRMED` is the state you want. The pool's policy is 12+ characters with upper, lower, number
and symbol.

#### Still outstanding on the AWS side

- **CORS** must list the CloudFront origin in `cors_allow_origins`, or every browser-head request
  to the DataFeed API fails preflight. Cognito's own endpoint sends permissive CORS, so sign-in
  itself works either way — it is the data calls that break. The desktop head is unaffected: not a
  browser, no origin.

The client sends the **access** token. The route's scope check is off (`api_required_scopes = []`)
because `InitiateAuth` access tokens carry only `aws.cognito.signin.user.admin` — custom resource
server scopes like `datafeed/read` are issued solely by the hosted UI's `/token` endpoint, so
requiring one would reject every token this app can obtain. The JWT authorizer still validates
issuer and audience, so an unauthenticated request never reaches the function.

### Testing sign-in

```bash
dotnet test
```

[`tests/`](tests/Terrafa.Continuum.Frontend.Tests/AuthenticationTests.cs) talks to the **real** user
pool, and needs no credentials: a deliberately wrong password proves the whole handshake ran,
because Cognito can only reject the SRP proof after accepting everything leading up to it. That
one assertion covers the region and client id resolving to a pool that exists, the app client still
permitting `USER_SRP_AUTH`, the SRP key schedule producing a well-formed claim, and the response
unmarshalling — each of which has its own failure message the test asserts it did *not* get. They
need outbound network, so CI without it runs:

```bash
dotnet test --filter Category!=Live
```

Opening a dataset costs one Athena query, billed on bytes scanned, because that is where the
leaf values come from. `DataFeedOptions.SampleValues` turns that off and leaves the schema
browsable; `MaxSampleColumns` caps how much of a wide table one query asks for, and leaves past
the cap are marked `no sample` rather than left looking empty.

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
