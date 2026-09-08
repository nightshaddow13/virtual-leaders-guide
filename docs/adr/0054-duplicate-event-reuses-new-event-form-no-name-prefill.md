# Duplicating an Event reuses the New Event form - no Name pre-fill, no auto-dedup

#116 (P2-21)'s own filed AC describes Duplicate as fully automatic: the new Event's Name is "the source's Name
(prefixed 'Copy of', de-duplicated with ' (2)', ' (3)', etc. if that's already taken)". Planning the
implementation surfaced real problems with that shape, and the design that replaced it turned out simpler than
what it replaced, not just different.

## The decision

Duplicate doesn't create anything itself, and doesn't compute a Name at all. Clicking it navigates to the
existing `/dashboard/events/new` route - the same page "+ New event" already opens - carrying the source
Event's Starts at/Ends at through the query string so the form opens with those two fields pre-filled. Name,
Slug, and Passcode all start blank, exactly as an ordinary new Event does. The Admin types the new Name
themselves; nothing suggests or auto-fills one. A Name collision on submit is the same 409 `EventEditor`
already turns into an inline field error for every create - the Admin retypes it themselves, with no automatic
suffix retry.

CONTEXT.md's `Duplicate` entry is updated accordingly: it previously listed Name among the fields copied from
the source ("copying another's fields (Name, Starts at/Ends at, ...) ... rather than typing one in from
scratch"). It now copies only Starts at/Ends at (and any future content) - the Name is always typed fresh.

## Considered options

- **Silent auto-generate + retry-on-conflict** (the issue's own filed design) - rejected on two grounds.
  First, a data-quality one: duplicating a duplicate stacks the prefix without limit ("Copy of Copy of Copy of
  X"), and nothing in the issue's AC addresses it. Second, a real correctness trap: `Event.Name` uniqueness
  excludes effectively-`Past`/`Cancelled` Events, but `Event.Slug` uniqueness is unconditional across every
  Status (ADR-0053) - since the candidate Slug derives from the candidate Name, a collision can come back as a
  409 naming *only* the `slug` pointer even though the `name` pointer is free. A retry loop that only advances
  on a `name`-pointer conflict would spin forever on exactly this case; getting it right meant retrying on
  either pointer, which is real logic with real edge cases (a retry cap, a truncation rule for a source Name
  near the 200-character column limit) for a naming scheme that also has the stacking problem above.
- **Real-time as-you-type availability checking** - rejected: `ApiEventClient.GetEventsAsync` has no Name
  filter today (only status), so this needs new Api query surface this story's own vertical slice doesn't
  otherwise call for.
- **A bespoke dialog** (Name + Starts at/Ends at fields, its own validation, its own 409/422 handling) opened
  from the Dashboard row, calling `CreateAsync` directly - rejected once it became clear this duplicates
  `EventEditor`'s own Create form field-for-field: required Name, the date-range rule (ADR-0042), and
  409/422-to-field-error handling (`ApplyFieldErrors`) all already exist there, tested, and reused by every
  other create. A second copy of the same validated form is exactly the near-duplicate logic this repo's
  coding standards call out on review.

## Consequences

- No new orchestration service, no retry loop, no truncation rule, no new Api surface. The Dashboard's
  Duplicate action is a single `NavigationManager.NavigateTo` call carrying two query values; `EventEditor`
  gains two `[SupplyParameterFromQuery]` properties consumed only on its `Id is null` path.
- #116's filed AC no longer matches what ships and needs its acceptance criteria edited to describe this
  behavior instead of the auto-generated-name one.
- Per ADR-0041's own documented gap, a `private [SupplyParameterFromQuery]` property can't be set from a bUnit
  component test - the query-driven date pre-fill is provable only end-to-end (`EventManagementScenarios`),
  the same limitation `SetupAccountShould`/`ResetPasswordShould` already carry for their own query parameters.
