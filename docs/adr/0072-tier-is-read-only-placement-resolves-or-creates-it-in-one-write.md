# Tab/Sub Tab/Section/Sub Section are read-only resources; Placement resolves-or-creates them in one write

P5-11 (#96) exposes `Tab`, `SubTab`, `Section`, and `SubSection` (ADR-0046) alongside `ActivityPlacement`
(ADR-0050's `Placement`, scoped to Activity). CONTEXT.md's Tier entry is explicit that none of the four is
"separately authored" - a Director never creates one directly; it comes into existence the moment a name is
typed while placing an Activity. We took that literally: each Tier's JSON:API resource is **read-only**
(`GenerateControllerEndpoints = JsonApiEndpoints.Query` - no `Post`/`Patch`/`Delete`), and
`ActivityPlacementResourceDefinition.OnWritingAsync` is the only code path that ever creates or deletes one.

A Placement create request carries, per level, either an existing Tier id or a raw name that doesn't exist
yet. `OnWritingAsync` resolves each level - an id round-trips to a lookup, a name resolves against the
Event's existing rows (case-insensitively) or creates a new row - before the Placement itself is saved, all
inside the one request/transaction that also runs the uniqueness check (ADR-0046) and the Tab/Sub-Tab
InfoPage-exclusivity check (ADR-0047). The Web client supplies either shape by first merging its own
in-session pending-ghost list against what it already loaded from the Tier `Query` endpoints (see
`ActivityEditor`'s builder) - the server never needs to know which case it's in.

## Considered options

- **Mirror `FacilityType`'s own-POST pattern** - give each Tier a full `[Resource]` with `Post`, and have
  the Web client resolve-or-create each level itself (up to 4 extra round trips) before POSTing the
  Placement with concrete ids, the same way `FacilityEditor.ResolveFacilityTypeIdAsync` already does for one
  level. Rejected: four independent round trips open a race window the single-level `FacilityType` case
  never has to worry about (two levels could each resolve against a stale read of the level above them), and
  a Tier having its own POST endpoint is a literal contradiction of CONTEXT.md's "not separately authored."
- **A dedicated "resolve Tier path" action endpoint**, called before the Placement POST to turn a mix of
  ids/names into concrete ids in one round trip, with the Placement POST then taking only ids - rejected:
  splits one logical write into two requests with no transaction spanning both, reopening the same race the
  option above has, just with fewer round trips instead of zero.

## Consequences

- `ActivityPlacementResourceDefinition.OnWritingAsync` is the single place that can create or delete a
  `Tab`/`SubTab`/`Section`/`SubSection` row - `TabResourceDefinition`/`SubTabResourceDefinition`/
  `SectionResourceDefinition`/`SubSectionResourceDefinition` need only `OnApplyFilter`'s Event-scoped read
  narrowing (mirroring `ActivityResourceDefinition.OnApplyFilter`), never an `OnWritingAsync` override - there
  is no write for one to authorize.
- The Placement create payload's wire shape is asymmetric with every other resource in this app: a per-level
  attribute pair (an optional id, a conditionally-required name) instead of a single `[Attr]` scalar FK. This
  is new, not a reuse of an existing idiom - `PlacementsResourceShould` needs explicit coverage for both the
  id-supplied and name-supplied shape at every level, not just one.
- P5-12 (#97)'s cascading Tier-reaping on removal, and P5-13 (#98)'s `SortOrder` PATCH, both still only ever
  touch Tier rows through `ActivityPlacementResourceDefinition`, not a Tier resource's own endpoint - this
  ADR's "one write path" rule extends to delete and reorder, not just create.
- None of `Tab`/`SubTab`/`Section`/`SubSection` (nor `ActivityPlacement.EventId`, ADR-0050) carries a foreign key
  to `Event`, and none has an `Event` navigation property (EF's conventions would wire a same-named
  `EventId` + navigation into a cascading FK on their own). Two independent reasons: SQL Server rejects a
  second cascade path to the same table, which `Section`'s two optional parents (ADR-0046's amendment) would
  force; and even a single-path cascade from `Event` into this subtree fails on SQLite (ADR-0014) whenever a
  Restrict FK points deeper into it (e.g. `ActivityPlacement`→`Tab`), because the engine may process the
  `Event`→`Tab` branch before the `Event`→`Activity`→`ActivityPlacement` branch has removed the referencing
  row. Deleting an Event therefore cleans up its Tier+Placement subtree explicitly, bottom-up, in
  `EventResourceDefinition.DeleteTierAndPlacementSubtreeAsync` - code the Event delete flow (P2-17, #112)
  gained as a consequence of this ADR, filtered on each table's denormalized `EventId`.
