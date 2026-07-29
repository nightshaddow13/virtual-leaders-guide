# Deploy pipeline authenticates to Azure SQL via managed identities, not connection-string secrets

`vlg-sqlserver` (provisioned under P1-8a, see [ADR-0005](0005-azure-hosting-topology.md)) was created with
`azureADOnlyAuthentication: true` — Entra ID–only, no SQL logins possible. This forces every connection to it,
both the Api's runtime connection and the deploy pipeline's migration step, to authenticate via Azure AD rather
than a connection-string secret. We run EF Core migrations (`dotnet ef migrations bundle`, a self-contained
executable in its own minimal image) as an **Azure Container Apps Job** inside `vlg-cae`, not from a
GitHub-hosted runner, so the SQL server's firewall never needs to admit public/GitHub IP traffic — the Job
reaches SQL only via the environment's existing Azure-service network path. The Job authenticates with its own
**user-assigned managed identity**, separate from the one `vlg-api` uses at runtime, so the always-on Api
service never holds the `db_ddladmin` rights the migration step needs — it's granted only
`db_datareader`/`db_datawriter`. Both identities are user-assigned rather than system-assigned so a stable
principal ID exists *before* first deploy, letting the one-time `CREATE USER ... FROM EXTERNAL PROVIDER` grant
(run manually by the server's Entra admin — Entra-integrated Azure SQL access can't be granted via ARM/RBAC)
happen once, ahead of any deploy, rather than needing to be redone if either identity's owning resource is ever
recreated.

## Considered options

- SQL-auth login for migrations, connection-string secret for Api runtime — not viable at all: the server is
  Entra ID–only, so SQL logins don't work regardless of preference.
- Running migrations from a GitHub-hosted runner via Entra Workload Identity Federation, with a per-run SQL
  firewall rule opened/closed around the runner's IP — rejected in favor of the Container Apps Job approach,
  which never exposes the SQL server to any public IP.
- One shared managed identity for both the Api app and the migration Job — rejected to keep the continuously
  running Api service off the schema-alteration privilege it never needs during normal operation.
- System-assigned managed identities — rejected because their principal ID doesn't exist until after the owning
  resource's first deploy, complicating the one-time Entra grant and making it non-idempotent across resource
  recreation.

## Consequences

No SQL-auth connection-string secret exists anywhere in this pipeline. Diagnosing a Container App's DB
connectivity failure means checking Entra/managed-identity role assignments, not a rotated secret. Standing up a
new environment (e.g. a second subscription for staging) requires re-running the one-time
`CREATE USER FROM EXTERNAL PROVIDER` grants by hand, since they aren't expressible in the Bicep template.
