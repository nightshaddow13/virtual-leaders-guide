# Design rationale moves to ADRs and `<remarks>`; bare `//` rationale blocks are gone

`docs/agents/coding-standards.md` previously endorsed a `//` header-block style for design rationale
alongside `///` ("both stay"), on the reasoning that rationale would bloat `<remarks>` hover tooltips if
migrated. In practice this produced roughly fifty such blocks, several duplicated near-verbatim at multiple
call sites (e.g. the env-var-vs-`ConfigureAppConfiguration` ordering hazard, repeated in three test classes),
with nothing keeping the copies in sync with each other or with the code they explain.

We decided no bare `//` rationale block survives. Each fact inside an existing block is triaged individually,
not the block as a whole, into one of four destinations. Applying the *same* three-part bar this repo already
uses for ADRs (hard to reverse, surprising without context, result of a real trade-off — see `docs/adr/`'s
own criteria) as a strict, all-three-required test: a fact that passes moves into an ADR (existing or new),
and the code keeps only a minimal `///` pointer line naming it — a signpost, not a restatement. A fact that
fails that bar but is still genuinely non-obvious becomes a `<remarks>` on the member it explains. A fact
already fully covered by an existing ADR keeps only the pointer, nothing else. A fact that's self-evident
from the code — or would be after a clearer name — is deleted outright, with no replacement anywhere.

Test classes get one exception to the "no `///` on tests" rule from `///` doc convention (P2-5, #14):
`<remarks>` is allowed on a test *class* for rationale that isn't shared test infrastructure, matching how
shared fixtures already carry docs. Individual `[Fact]`/`[Theory]` methods still get none — ADR-0012's
naming convention already makes the method name the documentation.

## Considered options

- **Keep the "both stay" convention (status quo)** — rejected: nothing stopped the same rationale from being
  restated at every site that needed it, as the duplicated ordering-hazard and 401-vs-403 blocks show, and a
  `//` block can silently go stale (see the `AppHostShould.cs` comment claiming zero resources are registered,
  long after P1-3/P1-4/P1-5 shipped SQL/Api/Web).
- **Route every fact into `<remarks>` uniformly, skip the ADR split** — rejected: architectural rationale
  (e.g. why `Web` forwards identity calls over HTTP instead of owning an `IdentityDbContext`, ADR-0022) needs
  one canonical, cross-file-discoverable home the way ADR-0006/0007/0022 already provide; pinning it to
  `<remarks>` on a single implementing class reintroduces the original "bloats every hover tooltip" problem
  this repo was trying to avoid, and leaves the decision invisible to any other call site that depends on it.

## Consequences

- Every relocated fact leaves a pointer at its code site — a `<summary>`/`<remarks>` line naming the ADR — so
  the reasoning stays reachable by reading the code, not only by searching `docs/adr/`.
- No hard length cap on `<remarks>`; density is allowed where trimming would lose safety-critical information
  (e.g. `VirtualLeadersGuideDbContext`'s CHECK-constraint block, which stays multi-line because compressing it
  invites someone to "simplify" the constraint and silently break SQLite compatibility, ADR-0014).
- Applying the strict three-part bar means most existing blocks fail it and land in `<remarks>`, not ADRs —
  only decisions with genuine considered alternatives (HTTP-vs-DbContext, deliberately omitting
  `IUserTwoFactorStore`) clear it; implementation quirks like a portable CHECK-constraint expression do not.
