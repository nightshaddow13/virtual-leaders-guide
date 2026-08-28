# Event lifecycle is one Status column - Draft, Live, Past, Cancelled - and it supersedes #103

Planning five new Phase 2 stories (deleting Events/Users/Grants, cancelling an Event early, duplicating one)
needed a real answer for what "archive" means, because the user's own request used it as an umbrella term
("archive events early (aka if they get canceled)") while the already-filed **#103 (P2-16: Auto-archive
Events past their end date)** defined archiving narrowly as *default-list visibility only* - explicitly not
deletion, not access revocation, and explicitly not touching the public Leaders Guide. A grilling session
(16 questions) worked out the actual state machine underneath both asks. This ADR records it and formally
supersedes #103, which is closed in favor of it.

## The model

`Event.Status` is `Draft` | `Live` | `Past` | `Cancelled`. `CONTEXT.md`'s `Status` entry is the canonical
definition of each value and the domain rules; this ADR covers the parts CONTEXT.md deliberately leaves out
(it stays free of implementation detail).

- **Only `Draft`, `Live`, and `Cancelled` are stored.** `Past` is never written to the column - it's computed
  at read time by comparing a `Live` row's `EndsAt` (ADR-0043) to now. This was a direct fork (grilling
  Question 12): the alternative was a background sweep flipping a stored value, rejected because it adds this
  app's first scheduled job and a window where an already-elapsed Event still reads `Live` until the sweep
  catches up. Computing it avoids both, at the cost of `EventResourceDefinition.OnApplyFilter` needing a query
  expression rather than a plain column filter to hide it from the default list.
- **Legal transitions are `Draft→Live` and `Live→Cancelled` only**, both manual, both Admin-only (ADR-0031).
  `Draft→Past`, `Live→Draft`, any client PATCH literally naming `Past`, and anything out of `Cancelled` are
  all illegal. A `Draft` Event's `EndsAt` elapsing does nothing to its Status (Question 2) - nothing was ever
  public to conclude. `Cancel` is only reachable from `Live` (Questions 5-6) - a `Draft` that isn't happening
  gets deleted, not cancelled, since there's no audience or record to preserve; a `Past` Event can't be
  cancelled retroactively, since "past" is already the accurate record at that point.
- **Both `Past` and `Cancelled` are terminal.** The only way back is duplicating the Event into a fresh
  `Draft` (Question 4) - cancelling is a fact about what happened, not a toggle to walk back.
- **Illegal transitions reject with 422**, validated in `EventResourceDefinition`, the same shape
  ADR-0042 already established for an invalid `StartsAt`/`EndsAt` range (Question 13) - both are business
  rules about one Event's own attributes, not a 409-shaped collision with another row.
- **Delete is a separate, independent action, available from any Status** (Question 7) - Cancel exists to
  keep a record and tell people; Delete exists for "this shouldn't exist at all." Forcing one through the
  other adds friction a confirm dialog already covers.
- **`Event.Name`'s unique index becomes filtered, excluding `Past` and `Cancelled`** (Question 17) - a
  `Past`/`Cancelled` row no longer blocks a new Event from reusing its Name, matching `Event.cs`'s own
  remarks anticipating exactly this. `Slug` is unaffected and stays permanently, unconditionally unique - it's
  the route key, never revisited by this ADR. The filtered-index SQL needs to build on SQLite as well as SQL
  Server (ADR-0014) - `VirtualLeadersGuideDbContext.ConfigureUserRoles`'s existing `HasFilter(...)` indexes are
  the pattern to follow, but their filter strings as written are SQL-Server-shaped and need checking against
  SQLite before reuse.

## This supersedes #103, deliberately

#103 said an archived Event's *"public Leaders Guide, direct-URL dashboard access, and Api resource all keep
working exactly as before."* That's still true for the automatic `Past` case here. It is **not** true for the
manual `Cancelled` case: cancelling takes the Event's public Leaders Guide dark (an AC added to #72, P4-2,
since the public guide itself doesn't exist yet). #103 treated "archived" as one undifferentiated concept;
this ADR splits it into two Status values precisely because they behave differently once a public guide
exists to darken. #103 is closed as superseded by this ADR and the stories built on it, following the
precedent Phase 5's epic set for #86/#91/#92.

## Considered options

- **A stored `IsArchived` flag orthogonal to `Status`** (Question 1) - rejected: no scenario needs archived-
  ness to vary independently of Status: an Event is hidden from the default list exactly when Status is
  `Past` or `Cancelled`, never otherwise. A second axis would just be two names for the same fact.
- **Deriving `Live` from `StartsAt`/`EndsAt` instead of a manual toggle** (Question 3) - rejected: it can't
  represent an Event with no dates set at all (a real, valid state per ADR-0043/P2-15), which would then have
  no way to ever leave `Draft`.
- **Letting `Cancel` apply from `Draft` or reverse from `Cancelled`** (Questions 4-6) - rejected for the
  reasons in "Legal transitions" above; both would blur Cancelled's meaning as a factual record.
- **No server-side transition validation, trusting the UI** (Question 13) - rejected: it would leave the
  entire state machine enforced only by Blazor markup, with a stray PATCH able to silently corrupt it.
- **Blocking Event deletion while Directors are still assigned, instead of cascading** (Question 14) -
  rejected: `UserRole.cs` had left the Event→Grant cascade explicitly provisional pending this exact
  decision; a Grant is meaningless once its Event is gone, so cascading is the considered choice, not the
  inherited default it started as.

## Consequences

- `EventResourceDefinition.OnApplyFilter` now needs a computed-`Past` expression for the default collection
  filter, not a plain column compare - see ADR-0043's own remarks on the app's existing UTC/viewer-timezone
  split for how "past" and "now" are already reconciled across timezones.
- `/api/events` gains a fourth non-2xx write outcome shape (422 for an illegal Status transition, alongside
  403/409/422-for-dates from ADR-0042) - a future Event-scoped business rule should keep reaching for 422 the
  same way, not overload 409.
- Deleting a User or an Event both interact with this Status model only incidentally (Delete is Status-
  independent, per Question 7) - neither P2-19 (delete a User) nor P2-17 (delete an Event) needs to check
  Status at all.
- #103 is closed. Whoever reopens Event archiving work in the future should read this ADR first, not #103.
