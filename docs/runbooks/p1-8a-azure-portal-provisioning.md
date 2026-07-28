# P1-8a: Provision Azure resources via the Azure Portal

Manual walkthrough for provisioning the resources from [ADR-0005](../adr/0005-azure-hosting-topology.md) by hand,
mostly via the Azure Portal (steps 2–3 use the `az` CLI instead — see why there). Do these in order — later steps
assume the resource group from step 1 exists.

`AppHost.cs`/`azd provision` no longer declares the Container Apps Environment or the Container Apps themselves,
since Aspire would auto-provision an ACR alongside the environment (see ADR-0005) — so steps 2–3 below must be
done separately either way. Azure SQL and Storage (steps 4–5) can alternatively be provisioned via `azd provision`
against the existing `AppHost.cs` declarations if you prefer; this walkthrough covers doing all of it by hand
instead.

Suggested naming: Azure resource names (storage account, SQL server) must be globally unique, lowercase, and
in some cases ≤24 characters, so they can't follow the full `VirtualLeadersGuide` project-naming convention from
[ADR-0010](../adr/0010-virtualleadersguide-project-naming-convention.md) (that ADR is scoped to code, not cloud
resource names). Examples below use a `vlg` prefix — swap in your own if you want something else, and pick one
Azure region up front and use it for every resource.

No Azure Container Registry is provisioned — per [ADR-0005](../adr/0005-azure-hosting-topology.md), container
images are built and pushed to GitHub Container Registry (ghcr.io) by a GitHub Actions workflow in P1-8b, and the
Container Apps here pull directly from ghcr.io. The packages are public, so no registry credential is configured
anywhere — Container Apps pulls public images with no authentication. This also means the Container Apps
Environment and the Container Apps themselves are created by hand below (not via `azd provision`/Aspire's
`AddAzureContainerAppEnvironment`, which would auto-provision an ACR alongside the environment) — see the ADR for
why.

## 1. Resource group

| Field | Value |
| --- | --- |
| Resource group | `rg-virtualleadersguide` (or your choice) |
| Region | Your chosen region (e.g. `East US 2`) — use this same region for every resource below |

## 2. Container Apps environment (Consumption)

The Azure Portal no longer offers a standalone "create environment" flow — the Container Apps Environment
creation blade is now only reachable from inside the "Create Container App" wizard, which forces you to create a
container app (and pick an image/registry) at the same time. Since P1-8a shouldn't create any Container Apps yet
(those come from P1-8b, pulling from ghcr.io), use the `az` CLI instead — it creates the environment standalone,
with no container app or registry required:

```powershell
az login   # if you haven't already

az containerapp env create `
  --name vlg-cae `
  --resource-group rg-virtualleadersguide `
  --location "East US 2" `
  --logs-destination none
```

Omitting `--enable-workload-profiles` leaves it at its default of `true` — Azure now creates every environment as
a "Workload profiles" environment with a built-in **Consumption** profile that can't be removed. As long as you
never add a Dedicated workload profile (`az containerapp env workload-profile add`), the environment behaves as
Consumption-only (scale-to-zero, no reserved capacity cost) — the same outcome ADR-0005 calls for.

*Note: ingress (Api internal-only, Web external) is configured per-Container-App, not on the environment — that
happens in the next step.*

## 3. Container Apps (`web`, `api`)

Per [ADR-0005](../adr/0005-azure-hosting-topology.md), these are created here — with their final ingress
config — and never re-created. P1-8b's deploy workflow only ever runs `az containerapp update --image ...`
against them; it never touches ingress. Omitting `--image` defaults each app to Azure's public quickstart
placeholder image until P1-8b's first deploy replaces it — that's expected, and fine to leave publicly reachable
in the meantime (Consumption plan, `--min-replicas 0`, so it costs nothing while idle).

```powershell
az containerapp create `
  --name vlg-web `
  --resource-group rg-virtualleadersguide `
  --environment vlg-cae `
  --ingress external `
  --target-port 8080 `
  --min-replicas 0

az containerapp create `
  --name vlg-api `
  --resource-group rg-virtualleadersguide `
  --environment vlg-cae `
  --ingress internal `
  --target-port 8080 `
  --min-replicas 0
```

`--target-port 8080` matches the default port the .NET SDK's container-publish support (`dotnet publish
/t:PublishContainer`) configures via `ASPNETCORE_HTTP_PORTS` — P1-8b's Dockerfile-less container build should not
override it. No `--registry-server`/`--registry-username` flags are needed since the ghcr.io packages are public.

## 4. Azure SQL Database (General Purpose Serverless, free offer, auto-pause)

Search **SQL databases** → **Create**.

**Basics tab:**

| Field | Value |
| --- | --- |
| Resource group | from step 1 |
| Database name | `virtualleadersguide` |
| Server | **Create new** — name `vlg-sqlserver<unique-suffix>`, same region as step 1, choose your authentication method (Microsoft Entra-only is recommended for new servers) |
| Want to use SQL elastic pool | No |

Near the top of the Basics tab, look for a banner/callout offering the free offer (**"Want to try Azure SQL
Database for free?"**) and select **Apply offer**. This unlocks a **Behavior when free limit reached** setting —
set it to **Auto-pause the database until next month** (not "Bill over usage"): once free vCore-seconds/storage
are exhausted for the month, the database goes inaccessible until the free quota resets rather than starting to
bill.

**Compute + storage** (click **Configure database**):

| Field | Value |
| --- | --- |
| Service tier | **General Purpose** |
| Compute tier | **Serverless** |
| Hardware configuration | **Standard-series (Gen5)** |
| Max vCores | **1** |
| Min vCores | **0.5** |
| Auto-pause delay | **60 minutes** (the minimum — pauses as soon as possible to minimize free-limit burn) |

**Networking tab:**

| Field | Value |
| --- | --- |
| Connectivity method | Public endpoint |
| Allow Azure services and resources to access this server | **Yes** — required for the Container Apps environment to reach the database until VNet integration is set up in a later issue |

Select **Review + create** → **Create**.

## 5. Storage account (Blob, Hot, LRS)

Search **Storage accounts** → **Create**.

**Basics tab:**

| Field | Value |
| --- | --- |
| Resource group | from step 1 |
| Storage account name | `vlgstorage<unique-suffix>` (globally unique, lowercase alphanumeric, ≤24 chars) |
| Region | same region as step 1 |
| Performance | **Standard** |
| Redundancy | **Locally-redundant storage (LRS)** |

**Advanced tab:**

| Field | Value |
| --- | --- |
| Access tier | **Hot** |

Leave everything else at its default. Select **Review + create** → **Create**.

No blob container needs creating yet — nothing in the codebase consumes blob storage today (see the P1-8a
implementation notes), so leave the account's blob service empty until a future issue actually needs one.

## Verification

Same `az` CLI checks as the `azd`-based path, once you're logged in (`az login`):

```powershell
$rg = "rg-virtualleadersguide"

az containerapp env show -g $rg -n vlg-cae --query "properties.workloadProfiles"
az containerapp show -g $rg -n vlg-web --query "properties.configuration.ingress.external"   # expect true
az containerapp show -g $rg -n vlg-api --query "properties.configuration.ingress.external"   # expect false
az sql db show -g $rg -s <your-sql-server-name> -n virtualleadersguide `
  --query "{sku:currentSku, minCapacity:minCapacity, autoPauseDelay:autoPauseDelay, useFreeLimit:useFreeLimit, freeLimitExhaustionBehavior:freeLimitExhaustionBehavior}"
az storage account show -n <your-storage-account-name> -g $rg --query "{sku:sku.name, kind:kind, accessTier:accessTier}"
```
