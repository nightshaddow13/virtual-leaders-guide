---
status: answers the per-resource write question ADR-0031 deferred, for Activity - the same call ADR-0059
  already made for InfoPage
---

# A Director may fully CRUD an Activity on an assigned Event, the same call ADR-0059 made for InfoPage

P5-6 (#87) exposes `Activity` (CONTEXT.md) as a JSON:API resource at `/api/activities`, scoped Admin/Director
the same way `/api/infoPages` already is. Scope is the entity itself (Name, Description, EventId) only -
Placement (which Tab, optionally Sub Tab/Section/Sub Section) is a separate resource, tracked as
P5-11/P5-12/P5-13 (#96/#97/#98).

ADR-0031 narrowed a Director's write access to Event details (Name/Slug/Passcode/Status/dates) to Admin-only,
and explicitly deferred the same question for every other Event-scoped resource, naming Activity by name as
one of the future cases. ADR-0059 already answered this for InfoPage, going the other way from Event details:
a Director assigned to an Event may create, read, update, and delete that Event's InfoPages. This ADR makes
the identical call for Activity, for the identical reason - CONTEXT.md's Director entry casts a Director as
the person who runs their assigned Event's Leaders Guide day to day, and Activity authorship is exactly that
kind of day-to-day write. Gating it through an Admin would recreate the bottleneck ADR-0031 was avoiding, one
resource later than InfoPage already crossed this line.

Delete carries no separate carve-out, for the same reason ADR-0059 gives for InfoPage: ADR-0045 makes every
destructive action a hard delete with no undo, but a Director can already destroy an Activity's content just
as permanently by PATCHing `description` to empty - nobody proposes gating that differently. The dashboard's
shared confirm dialog (ADR-0045), not an API-level role split, is the actual protection against accidental
loss, whenever a delete UI ships (P5-9, #95).

Codified as `ActivityAccessPolicy.CanWrite`, which is deliberately `EventAccessPolicy.CanRead`, not
`EventAccessPolicy.CanUpdate` - read and write coincide for Activity, unlike Event.

## This decision covers the full CRUD surface, not just Create

P5-6's own UI and acceptance criteria exercise Create only - there is no Activities list, edit page, or
delete action yet (P5-7/P5-8/P5-9, #93/#94/#95). But JsonApiDotNetCore generates a full CRUD controller from
one `[Resource]` class, so `ActivityResourceDefinition.OnWritingAsync` authorizes all four verbs
(Create/Read/Update/Delete) the moment the class exists - there is no way to ship Create's authorization
without also deciding Read/Update/Delete's. This ADR settles all four at once so P5-7/P5-8/P5-9 build on it
rather than re-deciding a question the code already answered uniformly on day one.

## Considered options

- **Amending ADR-0059 instead of a new ADR** - rejected: ADR-0059's title and body are specifically about
  InfoPage, and ADR-0031 frames this as "future Event-scoped resources... each make their own call." A
  separate ADR per resource keeps that convention; this one's body stays short by pointing back to ADR-0059's
  rationale rather than re-arguing it.
- **Deciding Create only and letting P5-8/P5-9 each amend this ADR later** - rejected: the code doesn't
  defer the decision (see above), so deferring it on paper would just mean re-opening a settled question
  twice more for no reason.

## Consequences

- `ActivitiesResourceShould` carries full CRUD test coverage (success and forbidden paths for every verb)
  from P5-6 onward, even though no UI exercises Read/Update/Delete yet - the authorization code they test
  exists in full regardless.
- Same authorization topology as InfoPage: `ActivityAccessPolicy` wraps `EventAccessPolicy` rather than
  re-parsing role claims, so the two can't drift apart from the Event-level access they both delegate to.
