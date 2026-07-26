# Test projects live in a top-level `tests/` folder, one per `src/` project

Test projects are placed in a top-level `tests/` folder, sibling to `src/`, rather than colocated inside `src/`
alongside the code they test. Within `tests/`, each `src/` project gets its own test project mirroring its name
(`VirtualLeadersGuide.AppHost` → `VirtualLeadersGuide.AppHost.Tests`, later `VirtualLeadersGuide.Web` →
`VirtualLeadersGuide.Web.Tests`, etc.), rather than one shared test project holding every test class. This is the
first test project added to the repo (P1-2), so — like ADR-0010 — the choice had no existing precedent to follow.
We picked `tests/` over nesting in `src/` to keep the solution's top-level folders read as "what ships" vs. "what
verifies it ship," and we picked one test project per `src/` project so each can carry independent test-only
dependencies (e.g. a future `Api.Tests` won't need whatever Blazor test-host packages `Web.Tests` needs) and so
`dotnet test` can be scoped to one concern at a time. Once several projects exist under this pattern, switching
either axis (folder location, or per-project vs. shared) means moving/renaming every test project and reference
between them — cheap now, increasingly disruptive as more test projects get added on top.

## Considered options

- Colocating tests inside `src/<Project>/Tests/` — keeps a project and its tests physically close, but blurs the
  "shippable code" vs. "verification code" boundary at the top level and has no precedent elsewhere in this repo.
- A single shared `VirtualLeadersGuide.Tests` project for all test classes — fewer `.csproj` files to manage, but
  forces every test project's dependencies (test-host packages, fixtures) into one project even when a given
  `src/` project's tests don't need them.
