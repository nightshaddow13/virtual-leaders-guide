# P1-8a: Provision Azure resources via the Azure Portal

Manual walkthrough for provisioning the resources from [ADR-0005](../adr/0005-azure-hosting-topology.md) by hand
in the Azure Portal, as an alternative to running `azd provision` against the `AppHost.cs` declarations. Do these
in order — later steps assume the resource group from step 1 exists.

Suggested naming: Azure resource names (storage account, ACR, SQL server) must be globally unique, lowercase, and
in some cases ≤24 characters, so they can't follow the full `VirtualLeadersGuide` project-naming convention from
[ADR-0010](../adr/0010-virtualleadersguide-project-naming-convention.md) (that ADR is scoped to code, not cloud
resource names). Examples below use a `vlg` prefix — swap in your own if you want something else, and pick one
Azure region up front and use it for every resource.

## 1. Resource group

| Field | Value |
| --- | --- |
| Resource group | `rg-virtualleadersguide` (or your choice) |
| Region | Your chosen region (e.g. `East US 2`) — use this same region for every resource below |

## 2. Azure Container Registry (Basic)

Search **Container registries** → **Create**.

| Field | Value |
| --- | --- |
| Resource group | from step 1 |
| Registry name | `vlgacr<unique-suffix>` (globally unique, alphanumeric only) |
| Location | same region as step 1 |
| SKU | **Basic** |

Leave **Admin user** disabled on the Access keys/Authentication tab — Container Apps will authenticate via managed
identity when Api/Web actually get deployed in P1-8b, not the admin account.

## 3. Container Apps environment (Consumption)

Search **Container Apps Environments** → **Create** (not the "Container Apps" wizard — that one bundles in
creating a container app you don't want yet).

| Field | Value |
| --- | --- |
| Resource group | from step 1 |
| Environment name | `vlg-cae` (or your choice) |
| Region | same region as step 1 |
| Zone redundancy | Disabled (unnecessary at this scale) |

On the **Workload profiles** tab: do nothing — leave it empty. Azure Portal now creates every environment as a
"Workload profiles" environment with a built-in **Consumption** profile that can't be deleted; as long as you
don't click **Add workload profile** to add a Dedicated profile, the environment behaves as Consumption-only
(scale-to-zero, no reserved capacity cost) — the same outcome ADR-0005 calls for.

Select **Review + create** → **Create**.

*Note: ingress (Api internal-only, Web external) is configured per-Container-App, not on the environment — nothing
to set here. That happens when the actual Container Apps get created in P1-8b.*

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
az acr show -n <your-acr-name> -g $rg --query "sku.name"
az sql db show -g $rg -s <your-sql-server-name> -n virtualleadersguide `
  --query "{sku:currentSku, minCapacity:minCapacity, autoPauseDelay:autoPauseDelay, useFreeLimit:useFreeLimit, freeLimitExhaustionBehavior:freeLimitExhaustionBehavior}"
az storage account show -n <your-storage-account-name> -g $rg --query "{sku:sku.name, kind:kind, accessTier:accessTier}"
```
