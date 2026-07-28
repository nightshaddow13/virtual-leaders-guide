# Azure hosting topology: Container Apps + Azure SQL Serverless + GHCR + Blob Storage

This is a small/personal-scale app, so hosting was chosen to minimize cost while staying on tooling .NET Aspire
supports natively. We decided on:

- **Azure Container Apps, Consumption plan** — scales to zero (idle cost $0), with a free grant (~180k
  vCPU-seconds / 360k GiB-seconds / 2M requests per month) that should cover this app's traffic entirely.
- **Azure SQL Database, General Purpose Serverless, using the free offer** — one free database per subscription
  (100k vCore-seconds/month + 32GB storage), auto-pauses when idle, and keeps the SQL Server/EF Core skill set
  already in use rather than switching database engines.
- **GitHub Container Registry (ghcr.io), free** — images build and push via GitHub Actions rather than Azure
  Container Registry, avoiding its ~$5/month Basic-tier floor. Aspire's `AddAzureContainerAppEnvironment`
  unconditionally auto-provisions an ACR alongside the environment with no supported way to point it at an
  external registry instead — the API that's meant to do this (`AddContainerRegistry` +
  `.WithContainerRegistry(...)` on the ACA environment) currently crashes Bicep generation
  (dotnet/aspire#14286, open). So the Container Apps Environment and the Container Apps themselves are
  provisioned outside Aspire's opinionated flow (`az` CLI — see
  `docs/runbooks/p1-8a-azure-portal-provisioning.md`), rather than `azd provision`. `AppHost.cs` keeps using
  Aspire for Azure SQL and Storage, which aren't affected by this bug.
- **Container Apps are created once, up front (P1-8a), then only ever updated (P1-8b)** — the runbook's
  `az containerapp create` calls set up `web`/`api` with their final ingress config (external/internal, per
  ADR-0002) and no `--image`, which defaults to Azure's public quickstart placeholder. P1-8b's GitHub Actions
  workflow then only ever runs `az containerapp update --image ghcr.io/...` on every deploy — it never touches
  ingress, so ADR-0002's public/internal split is set once, in an auditable manual step, not re-asserted in
  workflow YAML on every push. The ghcr.io packages are public (the repo itself already is, so a compiled image
  adds no meaningful exposure), which also means Container Apps needs no registry credential at all to pull
  them — public pulls are unauthenticated.
- **Azure Storage (Blob), Hot LRS** — for map images and other Event assets; negligible cost at this scale.
- **Path-based URL routing** (`yourdomain.com/e/{slug}`) for each Event's Leaders Guide, on one shared root
  domain, rather than per-event subdomains — subdomains would need wildcard DNS and TLS management for no real
  benefit at this scale.

## Consequences

Costs stay near $0–3/month at personal scale (SQL free offer + Storage only; no ACR floor), growing gradually
rather than jumping at a fixed-tier cliff if usage grows. The trade-off is a hand-rolled GitHub Actions deploy
step instead of Aspire's built-in `azd deploy`/`aspire deploy` — revisit once dotnet/aspire#14286 is fixed and
`AddAzureContainerAppEnvironment` supports an external registry without crashing.
