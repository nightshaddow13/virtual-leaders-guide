# P2-2: Persist Data Protection keys to Blob Storage

`vlg-web` runs at `min-replicas 0` ([ADR-0005](../adr/0005-azure-hosting-topology.md)), so the default
in-memory Data Protection key ring is regenerated on every cold start and deploy - silently signing out every
session and invalidating every outstanding password-reset link. This runbook wires `vlg-web` to persist keys
to the Blob Storage account P1-8a already provisioned (`vlgstorage<unique-suffix>` - nothing has consumed it
until now).

**Local development needs no steps here.** Unlike ACS Email ([P2-1](p2-1-acs-email-provisioning.md)), Blob
Storage already has a local emulator wired into `AppHost.cs` (`RunAsEmulator(azurite => ...)`), so
`.WithReference(blobs)` on the `web` project resource injects a working `ConnectionStrings__blobs`
automatically against Azurite - no Aspire parameter, no `dotnet user-secrets` step, nothing to fill in.

Assumes `rg-virtualleadersguide` and the storage account already exist (P1-8a) and `vlg-web` already exists
(P1-8b's runbook, [step 0](p1-8b-azure-deploy-setup.md#0-verify-vlg-apivlg-web-exist)).

## 1. Create the `dataprotection-keys` container

```powershell
$rg = "rg-virtualleadersguide"
$storageAccount = "<your-vlgstorage-account-name>"

az storage container create `
  --name dataprotection-keys `
  --account-name $storageAccount `
  --auth-mode login
```

`--auth-mode login` uses your own signed-in Entra identity to create the container (needs at least Storage
Blob Data Contributor on the account, or Owner/Contributor at the resource group) - this is a one-time setup
action, not how the app itself will authenticate (see below).

## 2. Retrieve the storage connection string

```powershell
$storageConnectionString = az storage account show-connection-string `
  -g $rg -n $storageAccount --query connectionString -o tsv
```

## 3. Deployed: Container Apps secret

```powershell
az containerapp secret set -g $rg -n vlg-web --secrets "storage-connection-string=$storageConnectionString"

az containerapp update -g $rg -n vlg-web --set-env-vars `
  "ConnectionStrings__blobs=secretref:storage-connection-string"
```

Same shape as `internal-api-key` and `acs-connection-string`: a Container Apps secret, never committed to
source control.

**Update (P2-6, #15; [ADR-0026](../adr/0026-api-owns-passcode-encryption-with-its-own-key-ring.md)): this no
longer applies to `vlg-web` alone.** `vlg-api` now also needs Blob Storage - `Event.Passcode` is encrypted at
the Api layer, with its own Data Protection key ring (`api-keys.xml`, isolated from Web's `keys.xml`) in this
same `dataprotection-keys` container. Repeat this step for `vlg-api`:

```powershell
az containerapp secret set -g $rg -n vlg-api --secrets "storage-connection-string=$storageConnectionString"

az containerapp update -g $rg -n vlg-api --set-env-vars `
  "ConnectionStrings__blobs=secretref:storage-connection-string"
```

**This is not optional polish - deployed `vlg-api` will fail to start without it.** `Event.Passcode`'s EF Core
converter throws if no `IDataProtectionProvider` is registered (fail-closed, so a Passcode is never silently
written as plaintext), and `AddDataProtection()` in `Program.cs` needs a working blob connection string to
persist its key ring. This corrects this runbook's earlier note above that `vlg-api` never touches Blob
Storage - that was true before P2-6 and no longer is.

## Why a connection string, not a managed identity

Same reasoning as [ADR-0021](../adr/0021-vlg-web-acs-email-connection-string-not-managed-identity.md)
(ACS Email): `vlg-web` still has no managed identity. Standing one up for two connection-string-shaped
dependencies (ACS, and now this) isn't worth it yet - if `vlg-web` ever needs a managed identity for another
reason, ADR-0021 already says to revisit consolidating both onto it then.

This corrects `p1-8b-azure-deploy-setup.md`'s *"No connection string or identity on `vlg-web` - it never
talks to SQL directly"* note: `vlg-web` now holds two connection strings (ACS, and this one), but still no
managed identity and still no SQL access - which is the part that note was actually protecting.

## Verification

```powershell
az storage container show --name dataprotection-keys --account-name $storageAccount --auth-mode login
```

Then, once `vlg-web` is running the P2-2 image: sign in, restart the `vlg-web` revision (or trigger a
redeploy), and confirm the session survives instead of being signed out - that's the actual failure mode this
runbook exists to prevent. `keys.xml` should appear in the `dataprotection-keys` container after the first
request that needs to protect or unprotect data (the cookie-signing key, at minimum).

Once `vlg-api` is also running its P2-6 image with the secret above: creating or reading an Event should
succeed rather than the request failing with an unhandled `InvalidOperationException`, and `api-keys.xml`
should appear in the same container alongside `keys.xml` after the first Event is created.
