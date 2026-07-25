# Azure hosting topology: Container Apps + Azure SQL Serverless + ACR + Blob Storage

This is a small/personal-scale app, so hosting was chosen to minimize cost while staying on tooling .NET Aspire
supports natively. We decided on:

- **Azure Container Apps, Consumption plan** — Aspire's `azd`/`aspire deploy` tooling targets Container Apps by
  default, and the Consumption plan scales to zero (idle cost $0), with a free grant (~180k vCPU-seconds / 360k
  GiB-seconds / 2M requests per month) that should cover this app's traffic entirely.
- **Azure SQL Database, General Purpose Serverless, using the free offer** — one free database per subscription
  (100k vCore-seconds/month + 32GB storage), auto-pauses when idle, and keeps the SQL Server/EF Core skill set
  already in use rather than switching database engines.
- **Azure Container Registry, Basic (~$5/month)** — GitHub Container Registry would be free, but current
  Aspire/`azd` tooling has an open bug (dotnet/aspire#14286) deploying Container Apps against an external
  registry, so we accepted the small ACR cost rather than fighting that.
- **Azure Storage (Blob), Hot LRS** — for map images and other Event assets; negligible cost at this scale.
- **Path-based URL routing** (`yourdomain.com/e/{slug}`) for each Event's Leaders Guide, on one shared root
  domain, rather than per-event subdomains — subdomains would need wildcard DNS and TLS management for no real
  benefit at this scale.

## Consequences

Costs stay near $5–8/month at personal scale, growing gradually (per-second/per-vCore-second billing) rather than
jumping at a fixed-tier cliff if usage grows. Revisit the ACR-vs-GHCR choice once the upstream Aspire/`azd`
external-registry bug is fixed.
