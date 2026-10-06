---
status: answers the Read/Write question ADR-0066 deferred for Facility, for P8-2 (#166)
---

# A Director may read every Facility; only an Admin may write one

P8-2 (#166) exposes `Facility` (CONTEXT.md) as a JSON:API resource at `/api/facilities`. ADR-0066 settled
that Facility is a global resource with no owning Event, and only committed to "likely Admin-only" for
write, explicitly leaving the rest - including Read - to whichever story implemented it. JsonApiDotNetCore
generates a full CRUD controller from one `[Resource]` class, so this story has to decide Read too, not just
Create, the same reason ADR-0069 settled all of Activity's CRUD in one pass rather than per-UI-story.

This ADR opens Read to any signed-in caller - Admin or Director, platform-wide or Event-scoped - while
keeping Create/Update/Delete Admin-only. The reason is forward-looking: a later story (the Activity Location
picker, drawn from CONTEXT.md's Location entry) needs a Director to see the Buildings/Rooms/Program
Areas/Campsites of the Facilities assigned to their Event, even though only an Admin ever authors a Facility
or its Locations. Settling Read now means that story builds on an already-open door instead of reopening
this ADR just to widen it.

Because a Facility carries no `EventId` (ADR-0066), Read here is binary rather than per-row: there is no
assigned-Events set to narrow a collection against the way `EventResourceDefinition`/`ActivityResourceDefinition`
narrow theirs. A caller with `FacilityAccessPolicy.CanRead` sees every Facility; a caller without it is
denied outright (403), never silently narrowed to an empty page.

Codified as `FacilityAccessPolicy.CanRead` (any parsed role claim, Admin or Director) vs. `CanWrite`
(Admin-only) - a flat policy that parses role claims directly rather than delegating to `EventAccessPolicy`
the way `ActivityAccessPolicy`/`InfoPageAccessPolicy` do, since there's no Event to delegate to.

## Considered options

- **Admin-only Read and Write** - rejected: matches #166's AC literally (only Create is tested there) and
  ADR-0066's framing most narrowly, but would make the eventual Location-picker story reopen this exact
  question to widen Read, for a resource that has no Event-scoping to make that widening any more nuanced
  than a flat yes/no.
- **Deferring Read to the Location-picker story, Admin-only for now** - rejected for the same reason ADR-0069
  rejected deferring Activity's Read/Update/Delete: the code doesn't actually defer the decision (a
  `[Resource]` class authorizes all four verbs from the moment it exists), so deferring it on paper would
  just mean re-deciding a question the code already answered.

## Consequences

- `FacilitiesResourceShould` carries Director-success coverage for Read alongside the Create tests #166's own
  UI exercises - the authorization code being tested exists in full regardless of which verbs the dashboard
  currently calls.
- `FacilityTypeResourceDefinition` does **not** reuse `CanRead` - every verb on `/api/facilityTypes`,
  including Read, stays Admin-only (ADR-0071's own posture), since the only consumer of that resource is the
  Admin-gated Facility create/edit form. A Director never needs to browse Facility Types the way they'll
  need to browse Facilities/Locations.
