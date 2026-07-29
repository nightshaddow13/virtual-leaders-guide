# P1-8b: One-time setup for the Azure deploy pipeline

The workflow YAML and Bicep templates for P1-8b are agent-authored and live in the repo
(`.github/workflows/build.yml`, `infra/*.bicep`). What can't be automated is granting that pipeline access to
things outside the repo — an Azure AD app registration, an OIDC trust, and (since `vlg-sqlserver` is
[Entra-ID-only](../adr/0016-entra-only-sql-managed-identities-for-deploy.md), not SQL-auth) two database grants
that only the server's Entra admin can make. Do these in order.

Assumes the P1-8a resources already exist (`rg-virtualleadersguide`, `vlg-cae`, `vlg-sqlserver`,
`virtualleadersguide` database) **and** that `vlg-api`/`vlg-web` have already been created with their final
ingress config via [the P1-8a runbook's step 3](p1-8a-azure-portal-provisioning.md#3-container-apps-web-api) —
per ADR-0005, `build.yml`'s deploy job only ever runs `az containerapp update --image`, it never creates the
apps or touches ingress. This runbook picks up from there: attaching the identities and one-time config
those `az containerapp create` calls didn't set.

## 1. Two user-assigned managed identities

Created upfront, with a stable identity that exists *before* the first deploy — see
[ADR-0016](../adr/0016-entra-only-sql-managed-identities-for-deploy.md) for why this is user-assigned rather
than system-assigned.

```powershell
$rg = "rg-virtualleadersguide"
$location = "eastus"

az identity create -g $rg -n vlg-api-identity --location $location
az identity create -g $rg -n vlg-migrations-identity --location $location

$apiIdentityClientId = az identity show -g $rg -n vlg-api-identity --query clientId -o tsv
```

## 2. Grant each identity access to the database

Entra-integrated Azure SQL access can't be granted via ARM or `az role assignment` — it requires a SQL
connection authenticated as the server's Entra admin (`xgoss@live.com`). Connect via the Azure Portal's Query
editor (Entra auth) or `sqlcmd`/Azure Data Studio with Entra MFA auth, targeting the `virtualleadersguide`
database, and run:

```sql
CREATE USER [vlg-api-identity] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [vlg-api-identity];
ALTER ROLE db_datawriter ADD MEMBER [vlg-api-identity];

CREATE USER [vlg-migrations-identity] FROM EXTERNAL PROVIDER;
ALTER ROLE db_ddladmin ADD MEMBER [vlg-migrations-identity];
ALTER ROLE db_datareader ADD MEMBER [vlg-migrations-identity];
ALTER ROLE db_datawriter ADD MEMBER [vlg-migrations-identity];
```

`vlg-api-identity` deliberately never gets `db_ddladmin` — schema-alteration rights belong only to the
deploy-time migration Job, not the continuously running Api.

## 3. Attach the identity and configure `vlg-api`/`vlg-web`

One-time setup against the apps the P1-8a runbook already created. Everything here is stable across deploys
(only the image tag changes per push), which is exactly why it lives here instead of in `build.yml` — matching
ADR-0005's "set once, in an auditable manual step" reasoning for ingress.

```powershell
az containerapp identity assign -g $rg -n vlg-api --user-assigned vlg-api-identity

$internalApiKey = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
az containerapp secret set -g $rg -n vlg-api --secrets "internal-api-key=$internalApiKey"
az containerapp secret set -g $rg -n vlg-web --secrets "internal-api-key=$internalApiKey"

az containerapp update -g $rg -n vlg-api --set-env-vars `
  "InternalApi__Key=secretref:internal-api-key" `
  "ConnectionStrings__virtualleadersguide=Server=tcp:vlg-sqlserver.database.windows.net,1433;Initial Catalog=virtualleadersguide;Authentication=Active Directory Managed Identity;User Id=$apiIdentityClientId;Encrypt=True;Connect Timeout=30;" `
  "ASPNETCORE_ENVIRONMENT=Production" `
  "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true"

$apiInternalFqdn = az containerapp show -g $rg -n vlg-api --query "properties.configuration.ingress.fqdn" -o tsv

az containerapp update -g $rg -n vlg-web --set-env-vars `
  "InternalApi__Key=secretref:internal-api-key" `
  "ASPNETCORE_ENVIRONMENT=Production" `
  "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true" `
  "services__api__https__0=https://$apiInternalFqdn"
```

`vlg-web`'s `services__api__https__0` wires Aspire's client-side service discovery
(`Program.cs`: `https+http://api`) to Api's internal FQDN by hand — there's no AppHost in the deployed
environment to supply it. No connection string or identity on `vlg-web` — it never talks to SQL directly.

Keep the generated `$internalApiKey` value somewhere retrievable (e.g. a password manager) — it's not stored in
GitHub, only as a Container Apps secret on both apps.

## 4. App registration + OIDC federated credential

```powershell
$app = az ad app create --display-name vlg-github-deploy | ConvertFrom-Json
az ad sp create --id $app.appId

az ad app federated-credential create --id $app.appId --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:nightshaddow13/virtual-leaders-guide:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Note the subject is scoped to `ref:refs/heads/main` — this covers both the `push`-triggered and
`workflow_dispatch`-triggered runs of `build.yml`, since both execute against `main`, but will **not** trust a
deploy triggered from any other branch or a pull request.

## 5. Role assignment

```powershell
$sub = az account show --query id -o tsv
az role assignment create `
  --assignee $app.appId `
  --role "Contributor" `
  --scope "/subscriptions/$sub/resourceGroups/$rg"
```

`Contributor` (not the narrower Container Apps Contributor) because the migration Job's `az deployment group
create`/Bicep step needs `Microsoft.Resources/deployments/write`, which Container Apps Contributor doesn't
include. The plain `az containerapp update --image` calls for `vlg-api`/`vlg-web` only need
`Microsoft.App/containerApps/write`, already covered by this same role.

## 6. GitHub repo configuration

**Settings → Secrets and variables → Actions → Secrets:**

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `$app.appId` from step 4 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `$sub` from step 5 |

No SQL credential and no `INTERNAL_API_KEY` — the deploy workflow never touches secrets or env vars, only image
tags, so nothing sensitive needs to live in GitHub at all. Both Container Apps and the migration Job
authenticate to SQL via their managed identities (ADR-0016); the Web↔Api shared secret is a Container Apps
secret set directly in step 3.

**Variables:**

| Variable | Value |
| --- | --- |
| `AZURE_RESOURCE_GROUP` | `rg-virtualleadersguide` |
| `CONTAINER_APPS_ENV` | `vlg-cae` |
| `MIGRATIONS_IDENTITY_NAME` | `vlg-migrations-identity` |

## 7. GHCR package visibility

After the first successful `build.yml` run has pushed images, go to each package's settings
(`github.com/users/nightshaddow13/packages/container/virtualleadersguide-api`, `-web`, `-migrator` →
**Package settings**) and set visibility to **Public**, linking each to this repository.

This is required for Container Apps to pull the images — Consumption-plan apps scale to zero and re-pull on
every cold start, so a credential tied to the workflow's short-lived `GITHUB_TOKEN` (used only for the *push*)
would work at deploy time but break later pulls once it expires. Since this repo is already public, a public
image built from that same public source discloses nothing new.

## Verification

```powershell
az identity list -g $rg -o table
az containerapp show -g $rg -n vlg-api --query "identity"
az ad app federated-credential list --id $app.appId -o table
az role assignment list --assignee $app.appId -o table
```

Then trigger `build.yml` via `workflow_dispatch` and watch it build, push three images, run the migration Job,
and update both Container Apps' images — see the plan's Verification section for what to check on the deployed
app.
