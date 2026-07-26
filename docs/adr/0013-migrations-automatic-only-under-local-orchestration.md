# Migrations apply automatically only under local Aspire orchestration, not in production

Local dev and automated tests need `Database.Migrate()` applied without a manual step — including in
ephemeral SQL containers spun up by `Aspire.Hosting.Testing` for automated tests, where there's no window
for a manual step between container start and test assertions. But auto-migrating from every `Api` process
startup in production risks races once there are multiple concurrent replicas. We gate the migration behind
an `AppHost`-injected `Migrations:ApplyAutomatically` config flag, set to `true` only when
`!builder.ExecutionContext.IsPublishMode` (true for `dotnet run`/`dotnet test` against `AppHost`, false for
`aspire publish`/`azd` deploy-manifest generation) — not `ASPNETCORE_ENVIRONMENT`/`IsDevelopment()`, since
`Aspire.Hosting.Testing`'s `DistributedApplicationTestingBuilder` doesn't reliably propagate launch-profile
environment variables to child project resources ([dotnet/aspire#5093](https://github.com/dotnet/aspire/issues/5093)),
making an `IsDevelopment()` gate flaky in our own test suite. Deployed environments must apply migrations as
an explicit deploy-pipeline step instead (tracked in P1-8b).

## Considered options

- Gating on `IsDevelopment()`/`ASPNETCORE_ENVIRONMENT` — the more conventional approach, but unreliable under
  `Aspire.Hosting.Testing` today per the linked upstream bug.
