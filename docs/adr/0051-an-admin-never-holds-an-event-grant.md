# An Admin never holds an Event Grant

ADR-0035 states Admin never has Grants - holding the Role already is full access to every Event, so a
separate Grant would be redundant, not a second permission. Nothing enforced that until now. P2-18 (#113)
found the gap while adding a "Remove" action to an Event's Directors list: a User invited as Director,
who later lands on the config admin allowlist (ADR-0008), holds both the Admin Role and an unscoped
Director Role. They still satisfy `IsDirector`, so they appear in `EventEditor.razor`'s "Add an existing
Director" picker, can be granted an Event-scoped Director Grant like any other Director, and that Grant is
then unremovable-with-effect: deleting it changes nothing they can see, since their Admin Role already
covers every Event.

`UserRoleResourceDefinition.OnWritingAsync` now refuses both create and delete of an Event-scoped Director
Grant (`RoleId == Director`, `EventId` set) for a User who separately holds an Admin row, alongside its
existing Admin-role-grant refusal (ADR-0033) - a different check, since that one is about the row being
written, and this one is about a *different* row the same User holds.

## Considered options

- **UI-only guard** (disable the row's remove control, no Api change) - rejected: a stale page still issues
  a successful DELETE, so the guard is cosmetic rather than a real invariant.
- **Delete-only** (refuse removing an Admin-held Grant, but still allow creating one) - rejected: it
  protects a bad row while doing nothing to stop new ones from forming, so the population of stuck rows
  only grows.
- **Create-only** (refuse forming the invalid state, but leave existing bad rows removable) - the most
  narrowly "correct" rule on its own, but it leaves the UI's disabled remove button inconsistent with what
  the Api would actually allow, and does nothing for rows that already exist.

## Consequences

- `/internal/authorization/users/{id}/grants/{grantId}` (`InternalAuthorizationEndpoints.DeleteGrantAsync`)
  is deliberately left unguarded - it stays the escape hatch for cleaning up any pre-existing invalid row,
  since `/api/roleGrants` can no longer delete one back out once ADR-0033's Admin-role-grant refusal and
  this rule both apply to the same write.
- `EventEditor.razor`'s remove control renders disabled (with an explanatory tooltip, ADR-0052) for an
  Admin-held row - a UI nicety layered on top of this Api rule, not a substitute for it. Because create is
  now refused too, that disabled state only ever fires on legacy data, never on a state this app can still
  produce.
- A follow-up (filed alongside P2-18) covers excluding Admins from the "Add an existing Director" picker
  outright - no longer a correctness fix, since the Api refuses the write regardless, just a UI nicety that
  avoids offering an action that will 403.
