# Computed `Past` can't be indexed, sorted by, or reliably filtered at the SQL level

ADR-0044 designed `Event.Status` around `Past` being computed at read time rather than stored, and left two
things unresolved for whoever built it: whether `Event.Name`'s uniqueness rule could really be a filtered DB
index, and whether the SQL-Server-shaped `HasFilter` strings `ConfigureUserRoles` already uses would parse on
SQLite (ADR-0014). Building P2-20 (#115) answered both, and surfaced a third, more consequential constraint
neither ADR-0044 nor the issue anticipated: EF Core's SQLite provider cannot translate a `DateTimeOffset`
inequality at all. All three trace back to the same root cause - JsonApiDotNetCore 5.11 and both target SQL
engines have real, checkable limits on what a value that's "sometimes computed" can participate in.

## SQLite cannot compare `DateTimeOffset` with `>`/`<`/`>=`/`<=` - only `==`/`!=`

This is the one that actually blocked the build, not just a design nuance. Confirmed against Microsoft's own
current documentation (fetched live while building this story, not a stale historical issue):

> SQLite doesn't natively support [`DateTimeOffset`]... EF Core can read and write values of these types, and
> querying for equality is also supported. Other operations, however, like comparison and ordering will
> require evaluation on the client.

`db.Events.Where(e => e.EndsAt > now)`, isolated from `Status` or anything else, throws
`InvalidOperationException: could not be translated` under the SQLite-backed Api test suite. SQL Server
(production) has no such gap - `datetimeoffset > datetimeoffset` is native. Nothing in this codebase filtered
on `StartsAt`/`EndsAt` via LINQ before this story, so nobody had hit it, and because the default collection
view unconditionally needed "is this Live row elapsed," it broke essentially every existing "list Events"
test, not just new ones, until diagnosed.

**Decision: `EventResourceDefinition` detects the active provider** (`_dbContext.Database.ProviderName`, a
plain string compare - not the `Database.IsSqlite()` extension, which lives in the
`Microsoft.EntityFrameworkCore.Sqlite` package the production Api project deliberately never references) and
degrades gracefully under SQLite:

- The default collection filter's `LiveNotElapsed` term drops its `EndsAt` comparison entirely, becoming just
  `Status == Live` - every stored-Live row, elapsed or not. An *approximation*, not a bug: SQLite genuinely
  cannot answer "elapsed" at the SQL level, so the alternative isn't a more-correct filter, it's a crash.
- An explicit `filter=equals(status,'Past')` (`LiveElapsed`) degrades to a deterministic *empty* result via a
  portable `0 = 1` comparison, rather than a wrong approximation. Comparing the non-nullable `Status` column
  to `null` was considered for this and rejected - it risks failing to even *build* the expression tree
  (`Expression.Equal` between a non-nullable value type and a null constant isn't valid without an explicit
  nullable conversion), not just failing to translate it.
- **Single-resource reads are unaffected.** `OnSerialize` computes `Past` in memory, after materialization, on
  whatever row was already fetched by id (equality only) - no inequality ever reaches SQL on that path. Same
  for `ValidateStatusTransitionAsync`'s pre-PATCH lookup and `CheckForConflictsAsync`'s Name-reuse check: both
  re-read via `AsNoTracking()` and compute the effective status in C# afterward, never in the `Where` clause
  itself.

**Consequence for testing**: the SQLite-backed `EventsResourceShould` suite fully verifies Status-only
behavior (transitions, POST rejection, Name-reuse after Cancel/Past, Director scoping composed with a status
filter) and single-resource `Past` computation - genuinely correct and fully covered. It **cannot** verify true
elapsed-exclusion at the *collection* level: the default list hiding an elapsed Live Event, or
`filter=equals(status,'Live'/'Past')` actually including/excluding one. That behavior is real and correct in
production; it's proven end-to-end against the real engine instead, by a dedicated scenario in
`EventManagementScenarios` (`VirtualLeadersGuide.E2E.Tests`, real SQL Server via Aspire).

**Considered and rejected**: converting `Event.StartsAt`/`EndsAt`'s mapped storage type from `datetimeoffset`
to `datetime` (Microsoft's own general recommendation for this limitation) would fix the translation gap
directly, but retroactively changes a P2-15/ADR-0043 decision - the columns' actual SQL Server type, and a
migration touching already-shipped data - to serve a P2-20 need. Rejected as out of this story's scope; if a
future story needs SQL-level date comparisons badly enough to justify it, that's ADR-0043's call to revisit
deliberately, not a side effect of this one.

## `Event.Name` uniqueness moves from a filtered index to application code

ADR-0044 said the unique index would be "filtered, excluding `Past` and `Cancelled`." That can't be built
either, for a related but distinct reason. `Past` is `Status = 'Live' AND EndsAt <= now()` - a filtered index
predicate must be deterministic on both SQL Server and SQLite, so no predicate can reference the clock. The
closest deterministic approximation, `Status = 'Draft' OR (Status = 'Live' AND EndsAt IS NULL)`, is also
rejected outright by SQL Server's own filtered-index grammar, which permits no top-level `OR` in a filter
predicate - only a conjunction of single-column comparisons.

A narrower index scoped to just the undated case (`Status <> 'Cancelled' AND EndsAt IS NULL`) was considered
and rejected: it's expressible, but it's a deterministic subset of the real rule, so it protects only Events
with no dates set, while every dated Event's Name uniqueness would rest entirely on application code anyway -
a DB backstop that covers a shrinking minority of rows isn't worth the "why is this index scoped so oddly"
question it leaves for the next reader.

The decision: **drop the unique index on `Event.Name` entirely.** Uniqueness among non-terminal Events (not
effectively `Past`, not `Cancelled`) is enforced solely in `EventResourceDefinition.CheckForConflictsAsync`,
the same method that already produces the 409 today. This mirrors `Event.Passcode`, whose own remarks already
note "no DB constraint could validate a plaintext shape anyway." Keeping any index filtered only on
`Status <> 'Cancelled'` (matching ADR-0044's literal wording without the `Past` clause) was rejected as worse
than either alternative: it's stricter than the app's own rule, so a create that `CheckForConflictsAsync`
allows could then violate the index and 500 instead of failing cleanly - the exact bug this design has to
avoid, not just an inconvenience.

## `status` keeps `AllowSort`, but sorts by the stored value

`sort=status` can only ever order by what's in the column - JsonApiDotNetCore's sort expressions have no way
to inject a computed "is this Live row actually past" comparison. An elapsed `Live` row therefore sorts
alongside current `Live` ones even though it reads back as `Past`. `AllowSort` stays on the attribute (the
issue asks for it, and a documented, well-defined ordering costs nothing to expose), but `Dashboard.razor`'s
STATUS column ships `Sortable="false"` so the one UI that would visibly demonstrate the mismatch never offers
it.

## The wire format, and every filter value, is PascalCase - not a style choice

JsonApiDotNetCore parses filter constants with `Enum.Parse(type, value)` using its case-sensitive overload, and
ships no default string-enum JSON converter - so `EventStatus` needed `[JsonConverter(typeof(JsonStringEnumConverter<EventStatus>))]`
on the type to serialize as text at all, and whatever casing that produces (PascalCase, from the enum member
names) is also the only casing `filter=equals(status,'...')` accepts. The issue's own dev notes write the
filter example lowercase (`filter=equals(status,'past')`); that 400s as written. Making lowercase work would
mean replacing JsonApiDotNetCore's `IFilterParser` in DI for a casing preference with no functional benefit -
nothing but this app's own Web client calls this endpoint. Accepted as PascalCase everywhere; the issue's
lowercase example is informal, not literal.

## Considered options

- **Filtered index matching ADR-0044's literal wording** (`Status <> 'Cancelled'`) - rejected: stricter than
  the app rule, so it can 500 on a create the app itself just approved.
- **Filtered index scoped to undated Events only** (`Status <> 'Cancelled' AND EndsAt IS NULL`) - rejected:
  real but shrinking protection, at the cost of an index whose scope needs its own explanation.
- **Converting `StartsAt`/`EndsAt` to `DateTime`-backed storage** - rejected as out of scope; see above.
- **Custom `IFilterParser` for case-insensitive status filters** - rejected: real code for a casing preference
  nothing consumes except this app's own client, which controls both sides of the wire already.
- **Dropping `AllowSort` instead of documenting its caveat** - rejected: the issue explicitly asks for it, and
  suppressing the one UI that would misuse it (`Sortable="false"`) costs one line.

## Consequences

- `Event.Name` collisions are a pure application-level 409 with no database backstop at all, for every Event
  regardless of whether it has dates set. A future story that wants a DB-level guarantee here needs a different
  approach entirely (e.g. an actual stored `IsActive`-style column maintained on every write), not a revival of
  the filtered-index idea this ADR closes off.
- Any future Event-scoped filter that needs a `DateTimeOffset` inequality inherits this same SQLite gap and
  needs the same provider-detection treatment (`EventResourceDefinition.CanCompareEndsAt`'s pattern) or an
  E2E-only verification strategy - this isn't specific to `Status`, it's a property of the column type on this
  provider.
- A JSON:API caller who sorts on `status` gets the raw stored ordering (`Cancelled < Draft < Live`
  alphabetically) with `Past` rows interleaved among `Live` ones - correct per this ADR, but worth restating
  wherever `Event.Status` is documented, since it's the one attribute on this resource where "what you asked
  to sort by" and "what you see" genuinely diverge.
- Every `EventStatus` value, in both request and response bodies and in every `filter=` query, is PascalCase
  (`Draft`/`Live`/`Past`/`Cancelled`). A future enum added to this Api that also needs JSON:API filtering
  inherits the same PascalCase constraint from the same framework version, not something specific to
  `Event.Status`.
