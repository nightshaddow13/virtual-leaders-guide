---
status: records P8-5 (#169)'s cascade criteria for the schema stories that have not built the dependents yet
---

# Deleting a Facility takes everything under it with it

P8-5 (#169) lets an Admin delete a Facility, and its acceptance criteria say what happens to whatever hangs off
one. None of those things exist yet: Program Areas, Buildings, Rooms, Campsites, Event assignments and an
Activity's Location all arrive in later stories. Nothing references a Facility today, so the criteria hold
vacuously and no test can pin them. This records the rule so the stories that add those tables honor it.

A Facility delete is never blocked by what hangs off it:

- Its Program Areas, Buildings, Rooms, Campsites and Campsite Areas are deleted with it.
- Its Event assignments are removed. The Events themselves are untouched.
- Any Activity Location pointing at a Location under it is cleared, never left dangling. The Activity itself
  is untouched.

This is the opposite of `Facility`→`FacilityType`'s `Restrict` (ADR-0071), and deliberately so: a Facility owns
its layout, which means nothing without it, while a Facility Type is only a label many Facilities share.

## What each later story owes

| Story | Foreign key behavior on a Facility delete |
| --- | --- |
| P9-1 (#171), P10-1 (#176), P10-5 (#180), P11-1 (#185) | `Cascade` |
| P12-2 (#191) | `Cascade` on the assignment row |
| P13-1 (#194) | `SetNull` on `Activity.LocationId`, or an equivalent explicit clear |

SQL Server refuses a schema with more than one cascade path to the same table, which Location's
table-per-type layout (ADR-0067) can easily produce. Where it refuses `Cascade` or `SetNull`, the story clears
or deletes the rows explicitly (in the resource definition, say) rather than weakening the rule.

Each of those stories also adds its own bullet to the Facility delete confirmation, with a count, shown only
when it applies (ADR-0045). Until then the dialog says only that the delete can't be undone.

## Considered options

- **Block the delete until the Facility is emptied** - rejected: removing a retired camp would mean deleting
  every Room and Campsite by hand first, and #169 says assignments never block.
- **Soft delete** - rejected for the reasons in ADR-0045.
