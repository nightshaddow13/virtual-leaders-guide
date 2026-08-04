---
status: narrows ADR-0011's one-test-project-per-src-project rule
---

# `VirtualLeadersGuide.E2E.Tests` lives in `tests/`, as a named exception to ADR-0011

ADR-0011 mirrors every test project 1:1 onto a `src/` project (`VirtualLeadersGuide.AppHost` →
`VirtualLeadersGuide.AppHost.Tests`, etc.), because each `src/` project's tests carry independent
dependencies and a distinct concern. P2.1-1 (#59) introduces a project that breaks that mirror on purpose:
its subject is the *composed system* — Api and Web running as separate processes against a real SQL Server
container and Azurite, driven by a real browser — not any single `src/` project. There is no `src/` project
to name it after, so `VirtualLeadersGuide.E2E.Tests` is the one exception to ADR-0011's naming rule, not a
new rule of its own (there is exactly one composed system to test, so this can't recur the way one-tool-per-
folder did for ADR-0023).

It stays under `tests/` rather than getting its own top-level folder the way `tools/` did (ADR-0023). `tools/`
needed a new folder because dev tooling is a genuinely different *kind* of artifact than a test — not
verification code, never run by `dotnet test`, never gated on CI's pass/fail. `E2E.Tests` is a test project
in every way that matters: it runs under `dotnet test`, produces pass/fail, and belongs in CI's test step
once P2.1-6 (#64) scopes it in. A new top-level folder would overstate how different it is from
`Api.Tests`/`Web.Tests`; a one-line naming exception describes it accurately.

## Considered options

- **A new top-level `e2e/` folder**, mirroring how `tools/` got its own folder (ADR-0023) — rejected because
  `tools/` earned that split by being a different *kind* of artifact (unshipped, not test-verified, not
  `dotnet test`-discoverable), which doesn't apply here: this project is a test project through and through.
- **Split E2E coverage across `Api.Tests`/`Web.Tests` instead of a new project** — rejected because neither
  boots the real Aspire-orchestrated stack (both run in-process against a fake host: SQLite for `Api.Tests`
  per ADR-0014, a synthetic `HttpContext` for `Web.Tests`'s `SignInShould`), which is the entire point of this
  ticket.

## Consequences

- `VirtualLeadersGuide.E2E.Tests` is the repo's first use of xUnit's `ICollectionFixture` /
  `[CollectionDefinition]`. Every other test class in the repo (`Api.Tests`, `AppHost.Tests`, `Web.Tests`)
  instead implements plain `IAsyncLifetime` and news up its own dependencies per class — cheap, because none
  of them boot a whole distributed application. Booting the full Aspire stack (SQL container + Azurite + two
  ASP.NET processes) is not cheap, so `AspireE2EFixture` is instead shared across the *entire* test run via
  one `AspireE2ECollection`.
- That sharing is a hard commitment, not just an optimization: every E2E test class must join
  `AspireE2ECollection` specifically. Declaring a second `[CollectionDefinition]` anywhere in this project
  would silently boot a second full Aspire stack alongside the first — doubling container/process startup
  cost and very likely deadlocking on the fixed launch-profile ports and the persistent SQL data volume
  `AppHost.cs` already declares. There is no compiler or test-runner error for getting this wrong; a reviewer
  has to catch it.
