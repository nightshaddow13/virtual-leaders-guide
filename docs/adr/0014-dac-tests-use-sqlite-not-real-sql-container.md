# DAC/logic-level tests use SQLite in-memory; only `AppHost.Tests` exercises the real SQL container

Automated tests of Api's data-access/business logic use EF Core's SQLite provider
(`UseSqlite("DataSource=:memory:")` + `Database.EnsureCreated()`) to build schema directly from the EF
model — fast, isolated, no Docker dependency — rather than replaying the actual SQL Server migration. This
is also a technical necessity, not just a preference: EF Core migrations are provider-specific, so a
migration generated against `UseSqlServer()` can't be validated by replaying it on SQLite anyway. Verifying
the real migration applies cleanly to actual SQL Server stays a system-level concern, covered by
`AppHost.Tests`'s existing Aspire-orchestrated test: since `Program.cs` runs `Database.Migrate()` before
`app.Run()`, a passing `/health` check there already proves the migration succeeded against the real
container.
