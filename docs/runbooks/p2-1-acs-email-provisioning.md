# P2-1: Provision Azure Communication Services Email

This runbook stands up the two Azure resources the app needs to send email: an Email Communication
Services resource holding an Azure-managed sender domain, and an Azure Communication Services (ACS)
resource linked to it that the app actually authenticates against. Like `internal-api-key`
([P1-8b](p1-8b-azure-deploy-setup.md)), the credential this produces is a configuration value, never
committed to source control — user-secrets locally, a Container Apps secret when deployed.

**No email-sending code lands here.** This is infrastructure only; `IEmailSender` and message content
belong to P2-2 (#11, password reset) and P2-12 (#43, Director invite).

Assumes `rg-virtualleadersguide` already exists (P1-8a). Unlike the Entra App Registration this ticket
replaced (see ADR-0019), provisioning ACS needs only a standard Contributor role on the resource group — no
tenant-admin rights — so this whole runbook is agent-executable.

## 0. Install the `communication` CLI extension

`az communication` isn't in core `az` — it ships as an extension.

```powershell
az extension add --name communication
```

## 1. Create the Email Communication Services resource

```powershell
$rg = "rg-virtualleadersguide"

az communication email create `
  --name vlg-email --resource-group $rg `
  --location "Global" --data-location "United States"
```

`Global`/`United States` aren't a real choice here — email resources are always `Global`, and `United
States` is the only `--data-location` this app has any reason to pick.

## 2. Provision an Azure Managed Domain

An Azure Managed Domain gives a working `*.azurecomm.net` sender domain instantly, with no DNS records to
add and no verification wait — the trade-off ADR-0020 already accepted (send-rate cap, and a From address
that reads `DoNotReply@<guid>.azurecomm.net` rather than a branded domain).

```powershell
az communication email domain create `
  --domain-name AzureManagedDomain `
  --email-service-name vlg-email --resource-group $rg `
  --location "Global" --domain-management AzureManaged
```

`AzureManagedDomain` is a fixed name Azure expects for this domain type — not a name we chose.

## 3. Create the ACS resource, linked to the domain

The app authenticates to *this* resource, not the Email Communication Services resource from step 1 — the
domain has to be linked in at creation (or via `az communication update --linked-domains`) before ACS will
send through it.

```powershell
$domainId = az communication email domain show `
  --domain-name AzureManagedDomain --email-service-name vlg-email -g $rg --query id -o tsv

az communication create `
  --name vlg-acs --resource-group $rg `
  --location "Global" --data-location "United States" `
  --linked-domains $domainId
```

## 4. Retrieve the sender address and connection string

```powershell
$senderDomain = az communication email domain show `
  --domain-name AzureManagedDomain --email-service-name vlg-email -g $rg --query fromSenderDomain -o tsv

$acsConnectionString = az communication list-key -g $rg -n vlg-acs --query primaryConnectionString -o tsv

"Sender address: DoNotReply@$senderDomain"
```

`fromSenderDomain` is the generated `<guid>.azurecomm.net` — the app's From address is
`DoNotReply@<that domain>`. Confirm it's the only valid sender username for this domain:

```powershell
az communication email domain sender-username list `
  --domain-name AzureManagedDomain --email-service-name vlg-email -g $rg -o table
```

**Hand the sender domain back** so it can be filled into `Email:SenderAddress` in
`src/VirtualLeadersGuide.Web/appsettings.json` — that value isn't a secret, it's the public From header, so
it belongs in source control. **Do not hand back `$acsConnectionString`** — that one goes straight into
user-secrets, next.

## 5. Local development

```powershell
dotnet user-secrets set "Parameters:acs-connection-string" "$acsConnectionString" `
  --project src/VirtualLeadersGuide.AppHost
```

`acs-connection-string` is a *required* Aspire parameter (`AppHost.cs`) — the AppHost fails to start if
it's unset, the same fail-closed shape `internal-api-key` already uses (ADR-0015). There's no dev/prod
split for this resource (single-environment project) — this is the same `vlg-acs` resource the deployed app
uses.

## 6. Deployed: Container Apps secret

```powershell
az containerapp secret set -g $rg -n vlg-web --secrets "acs-connection-string=$acsConnectionString"

az containerapp update -g $rg -n vlg-web --set-env-vars `
  "Email__ConnectionString=secretref:acs-connection-string"
```

`vlg-web` only — `vlg-api` never sends email, so it never receives this secret or env var. Keep the
generated `$acsConnectionString` value somewhere retrievable (e.g. a password manager) — like
`internal-api-key`, it's not stored in GitHub, only as a Container Apps secret on `vlg-web`.

## Why a connection string, not a managed identity

See [ADR-0021](../adr/0021-vlg-web-acs-email-connection-string-not-managed-identity.md) — `vlg-web` has no
managed identity today, and this keeps it credential-shaped exactly like `internal-api-key` rather than
standing up new infrastructure for a single call path.

## Verification

```powershell
az communication show -g $rg -n vlg-acs --query properties.linkedDomains
```

should return the `AzureManagedDomain` resource id from step 3. Then send a real test message — a
linked-but-misconfigured domain fails silently otherwise, so this step isn't optional:

```powershell
az communication email send `
  --connection-string $acsConnectionString `
  --sender "DoNotReply@$senderDomain" `
  --to "<your-own-inbox>@example.com" `
  --subject "P2-1 verification" `
  --text "If this arrived, vlg-acs can send."
```

Confirm it lands in your inbox (check spam — an Azure Managed Domain has no SPF/DKIM reputation history
yet).
