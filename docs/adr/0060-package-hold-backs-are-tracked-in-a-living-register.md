---
status: living register — entries are added when a package is deliberately held back and deleted in the
same commit that releases the hold. Every other ADR in this repo is a fixed record of a past decision;
this one is not, and it says so here so a reader doesn't have to guess.
---

# Deliberate package version hold-backs are tracked here, not in code comments

A `Directory.Packages.props` refresh routinely finds newer versions the repo chooses not to take yet — not
because they're broken, but for a specific, checkable reason. That reasoning used to have nowhere durable to
live: a version-pinned `<PackageVersion>` line with no comment looks identical to one nobody has looked at in
months, and this repo has already been bitten once by a stale rationale comment going undetected (`e8f2388`,
fixing a stale `Microsoft.Data.SqlClient` pin comment in this same file).

Each fact — one held-back package, one reason, one concrete unblock condition — is triaged individually
against this repo's own three-part ADR bar, the same way ADR-0030 requires for any design rationale. A hold
that's easy to reverse, unsurprising, or not really a trade-off doesn't get an entry; it's just the current
version. What earns an entry here is a deliberate *no* that a future refresh will otherwise re-litigate from
scratch.

**Reviewed on each package refresh, not on a calendar.** Refreshes here are event-driven, not scheduled, and
a stated cadence nobody honours is worse than none — see the register itself for whether it's actually being
kept current.

There's no GitHub issue for a hold's unblock condition to cite (this repo's dependency-bump precedent,
`uow/aspire-13.5.3`, referenced none either) — the condition itself is the citable thing, and unlike an
issue link, a condition can't go stale.

## Current holds

| Package | Held at | Latest available | Why | Released when |
|---|---|---|---|---|
| `xunit` | 2.9.3 | xunit.v3 4.0.1 | `Microsoft.Playwright.Xunit` 1.62.0 binds `xunit.extensibility.core` 2.8.0, and no `Microsoft.Playwright.Xunit3` package exists on NuGet — migrating to v3 means hand-rolling `E2ETestBase`'s Playwright fixtures instead of using the package's own base classes. | Playwright ships an xunit.v3-compatible test package. |
| `xunit.runner.visualstudio` | 3.1.5 | 4.0.0 | Conservatism, not a hard constraint: 4.0.0's own nuspec claims support for xUnit.net v1, v2, and v3 tests, so it would likely work today. Held back in step with the `xunit` hold above rather than taking the major alone. | `xunit` moves to v3, or a fix that matters here lands only in the 4.x line. |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.19.2 | 8.22.0 | `Microsoft.AspNetCore.Authentication.JwtBearer` still pulls `Microsoft.IdentityModel.JsonWebTokens` 8.19.2 transitively (verified against 10.0.12). Web mints the internal JWT (ADR-0007) and Api validates it; holding Web at the exact version JwtBearer resolves keeps minting and validating on an identical IdentityModel stack, per the comment on this package in `Directory.Packages.props`. | `Microsoft.AspNetCore.Authentication.JwtBearer` bumps its own transitive `Microsoft.IdentityModel.JsonWebTokens` dependency past 8.19.2. |

## Considered options

- **A comment on each held `<PackageVersion>` line, no ADR** — rejected: this is the status quo that already
  went stale once (`e8f2388`), and a comment on a build-file line is invisible to anyone not already reading
  that exact line.
- **One ADR per hold, following the usual immutable pattern, superseded when released** — rejected: three
  holds sharing the same shape (package, reason, unblock condition) don't need three files, and a hold that
  lasts one refresh cycle doesn't deserve the ceremony of a full superseding ADR to close it out.
- **No record at all, re-derive the reasoning on each refresh** — rejected: the research to reach these
  conclusions is real work (checking exact transitive floors, reading upstream release notes), and losing it
  every refresh means paying that cost repeatedly for a static answer.
