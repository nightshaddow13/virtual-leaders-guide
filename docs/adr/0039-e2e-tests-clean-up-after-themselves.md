# E2E tests clean up after themselves

`VirtualLeadersGuide.E2E.Tests` runs against a real SQL Server container with `.WithDataVolume()`
(`AppHost.cs`), so the database survives every run - and until now, nothing the suite created was ever
deleted. Across 25 facts that's roughly 20 Users and 10 Events accumulating *per run*, forever. The damage was
already visible in the code before any of this: `EventManagementScenarios` had to page through up to 50 grid
pages hunting for the Event it just made, because the Admin grid "lists every Event this local dev machine's
persistent SQL volume has ever accumulated across every past run of this suite"; `AspireE2EFixture`'s Admin
email had to be freshly guid-suffixed every run, because a fixed one would 409 the second time the suite ran
against the same volume. Every workaround like that exists only because nothing cleaned up.

The fix has two parts. First, a fixed, small set of fixture data - one Admin, one Director, one no-role
account, one pending Invite, one Event - gets created once and is never deleted; every other test either
reuses these directly or creates its own throwaway data and deletes it in `DisposeAsync`. Second, a run-end
sweep in `AspireE2EFixture.DisposeAsync` catches anything a crashed or killed test left behind. Fixed fixture
data is what makes reusing a *known* account or Event possible at all - `SignInAsAdminAsync`'s own reasoning
for the Admin email applies uniformly now, not as a one-off exception.

Nothing outside `@example.test` is ever touched. `.test` is an RFC 6761 reserved TLD - no real signup can ever
land there - so this is the entire containment mechanism; there's deliberately no second maintained list of
"protected" addresses alongside it; a filter that can never fire on real data is exactly the kind of dead,
misleading code ADR-0030 exists to keep out. The same idea needed one real code change to reach parity for
Events: unlike every test-created User email, a test-created Event's `Name` (`"Summer Camporee {guid}"`, etc.)
carried no discriminating prefix at all, so a name-based sweep filter would have matched none of them. Every
Event this suite creates now gets its `Name` prefixed with `e2e-`.

## The rule

After any completed run, the database holds exactly this and nothing else:

| Concept | Retained | Identity |
|---|---|---|
| Admin account | 1 | `e2e-admin@example.test` - in the AppHost `admin-allowlist` parameter |
| Director account | 1 | `e2e-director@example.test` - unscoped Director role **+** a grant on the retained Event |
| No-role account | 1 | `e2e-norole@example.test` - no grants at all |
| Pending Invite | 1 | `e2e-invited@example.test` - no password (`HasCredential == false`), unscoped Director role, no grant |
| Event | 1 | Name `e2e-retained-event` - the literal constant, distinguishable from every transient `e2e-<label>-<guid>` Event by carrying no guid suffix |
| Role grants (`UserRole`) | 3 | Director-unscoped x2 (director + invited), event-scoped x1 (director -> retained Event). Admin's grant is config-owned and resynced every login (ADR-0008), so it is never seeded here |
| Deleted Event (P2-17, #112) | 0 | An Event a delete scenario creates and then deletes through the UI action under test - self-cleaning by the assertion path itself, not `TrackEvent`+`DisposeAsync` teardown. Its Grants go with it (ADR-0044's cascade); the retained `e2e-retained-event` above is never a delete scenario's target |
| Anything else | 0 | Deleted by the owning test or the run-end sweep, and confirmed by `AspireE2EFixture.DisposeAsync`'s own verification step |

An Invite is a full `AspNetUsers` row (see `CONTEXT.md`'s Invite entry), not a lighter-weight thing - the
fixture total is 4 accounts, not "3 plus a bonus invite."

A test whose whole subject is what an identity can see or do (e.g. which Events a Director's dashboard lists)
still creates its own throwaway account rather than reusing a fixture one, even where nothing today would
technically break by sharing - that class of assertion is exactly what a later test is likely to tighten into
an exact-count check, and a shared identity whose visible state depends on what some other test file granted
it earlier is a coupling bug worth avoiding before it bites, not after.

`AspireE2EFixture.DisposeAsync` tears down in two phases. Its sweep runs first and is best-effort and logged
only, matching this project's existing teardown discipline (ADR-0028: a capture/cleanup failure must never
fail an already-decided *test's* result). It is not what makes the "nothing else survives" rule real - the
verification step that runs immediately after it is: a direct count assertion against the database, allowed
to throw. That throw is safe here specifically because it belongs to the collection *fixture's* teardown, not
any individual test's - xUnit surfaces it as its own distinct run-level error rather than corrupting a test
result, which is the thing ADR-0028 actually protects. (An earlier draft planned a dedicated `[Fact]` for this
instead, ordered to run last; dropped because xUnit v2 doesn't guarantee execution order across test classes
sharing one collection, only that they run sequentially - some order, not a chosen one. The fixture's own
teardown is the one point with a real "runs after everything" guarantee.)

`VLG_E2E_KEEP_DATA=1` disables per-test cleanup and the run-end sweep together, as one switch - not select
pieces of it. It exists for interactive debugging against a real, inspectable database instead of only a
`trace.zip`. Fixture seeding always runs regardless, KEEP_DATA or not - it has to be idempotent anyway (an
ordinary run must survive finding its fixture accounts already seeded by the run before it), so there was
nothing left for the flag to usefully skip there.

When a future story introduces a concept with no row in the table above - Activities, Pages, InfoPages, and
Placements are all coming - the retention amount is a question to ask during planning, not a default to
assume. Guessing zero, or guessing "however many the tests happen to need," is not allowed; the answer gets a
row here in the same change that introduces the concept.

## Considered options

- **Drop `.WithDataVolume()` for E2E** - rejected: `AppHost.cs` is the same file `dotnet run` local dev boots
  from, and this fixture boots that same AppHost. Fixing tests by degrading the dev inner loop is the wrong
  trade.
- **Truncate the whole database between runs** - rejected: it would take a developer's own hand-made accounts
  and Events with it, which is exactly what must never happen.
- **Per-test cleanup with no sweep** - rejected: a Ctrl+C or a crashed test body leaks silently, and silent
  leaks are how this problem existed in the first place.
- **A maintained `ProtectedEmails` allowlist alongside the `@example.test` domain filter** - rejected: the
  domain filter alone already excludes everything such a list would protect, so the list could never actually
  fire. A second, unreachable safeguard reads as load-bearing when it isn't.

## Consequences

- Every E2E scenario class either reuses one of the four fixture accounts / the one retained Event, or creates
  its own and tracks it for `E2ETestBase.DisposeAsync` to delete - there is no third option that leaves data
  untracked.
- `EventManagementScenarios.AssertEventVisibleInGridAsync`'s 50-page search is gone; the Admin grid is small
  and deterministic enough to assert against page 1 directly.
- `VLG_E2E_KEEP_DATA=1` is a documented, deliberate way to violate this policy for local debugging - not a bug
  if data is found sitting around after a run made with it set.
