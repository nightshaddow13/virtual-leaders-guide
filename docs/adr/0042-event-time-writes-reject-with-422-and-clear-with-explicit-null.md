# Invalid Event time ranges reject with 422; clearing a set time sends an explicit `null`

P2-15 (#102) added `Event.StartsAt`/`EndsAt` - both optional, but `EndsAt` may only be set once `StartsAt`
already is, and must be strictly after it. Two decisions fell out of building the write path for these two
attributes, neither of which had a precedent on `/api/events` before this ticket.

## 422, not 409, for an invalid range

`EventResourceDefinition.CheckForConflictsAsync` already throws a `JsonApiException` with `HttpStatusCode
.Conflict` (409) when a Name or Slug collides with another Event. `ValidateDateRange` throws a new kind of
error instead - `HttpStatusCode.UnprocessableEntity` (422), this app's first use of that status - naming
whichever attribute is wrong (`/data/attributes/startsAt` when `EndsAt` is set with no `StartsAt`;
`/data/attributes/endsAt` when `EndsAt` doesn't come strictly after `StartsAt`).

409 means "this collides with another Event" - a fact about two rows relative to each other. An invalid date
range is a fact about one Event's own two attributes; it has nothing to do with any other row in the table.
Reusing 409 for it would mean the status code stops telling a caller anything specific - "409" would just
mean "some kind of Event write went wrong," collapsing two genuinely different failure classes a client
might want to handle differently (a Conflict is often resolvable by picking a different Name/Slug; an
Invalid range means the caller's own two values disagree and needs to fix one relative to the other).

`ApiEventClient` mirrors this with a new `EventWriteOutcome.Invalid`, read and routed to the offending form
field by `EventEditor`'s `ApplyFieldErrors` exactly the way `Conflict`'s pointers already are - the two
outcomes share a pointer-reading contract, they just mean different things.

## Clearing a set time requires an explicit `null`, not omission

Every other `EventAttributesDto` property (`Name`, `Slug`, `Passcode`) is omit-on-null:
`ApiEventClient` serializes with `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`, so a
`null` C# value is left off the wire entirely, meaning "leave unchanged" - not "clear the column," which
would try to null a `NOT NULL` column for those three. `StartsAt`/`EndsAt` are the opposite: both carry
`[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`, so a `null` always serializes as an explicit JSON
`null`, and there is no way to express "leave unchanged" for either through `ApiEventClient.UpdateAsync` -
every PATCH sends whatever the caller currently has (`EventEditor` always passes both, converted from its
form model, whether or not either was actually edited this save).

This is deliberate: unlike `Name`/`Slug`/`Passcode`, an Event's dates are genuinely nullable - "no known
dates yet" is a real, valid state P2-15's acceptance criteria require rendering as blank, never an error or
a default. Sending an explicit `null` is the only way a client can ask to clear a date that was previously
set. The alternative - leaving clearing unsupported - would mean an Admin who mistyped a date could only
ever overwrite it with another date, never remove it, which contradicts that same acceptance criterion.

## Considered options

- **Reuse 409 for an invalid range** - rejected: smaller diff, and `EventEditor`'s client already had a
  409-pointer-routing path to reuse verbatim, but conflating "collides with another Event" and "your own two
  values disagree" under one status code makes 409 on this resource mean nothing in particular going
  forward.
- **Leave date-clearing unsupported, matching `Slug`/`Passcode`'s "omit means unchanged" shape** - rejected:
  the acceptance criteria are explicit that unset dates are a valid destination state, not just a valid
  starting one; a write path that can only add dates and never remove them doesn't fully satisfy that.

## Consequences

- `/api/events` now returns three non-2xx write outcomes with different meanings: 403 (not an Admin), 409
  (Name/Slug collision), 422 (invalid date range) - a future Event-scoped resource with its own business
  rules should reach for 422 the same way, not overload 409.
- `ApiEventClient.UpdateAsync` always sends `startsAt`/`endsAt` on every PATCH, even when a caller only
  changed the Name or Slug - unlike its other four parameters, there is no "this wasn't touched" value for
  either. `EventEditor`'s `CreateAsync` follows the same rule for its own follow-up PATCH (setting a custom
  Slug/Passcode on a newly created Event): it must also re-send whatever dates were set on the initial POST,
  or that follow-up PATCH would silently clear them.
