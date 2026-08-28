# Destructive actions hard-delete, confirmed through one shared dialog

Phase 2 gains its first three delete/remove actions (an Event, a User, a Director's Grant on an Event). None
of `DialogService.Confirm` or any reusable confirm component existed before this - the app's one prior
destructive action, "Revoke invite" on `UserDetail.razor`, fires with no confirmation at all. This records
the conventions the first of the three (deleting an Event) establishes for the other two to reuse.

## The model

- **Hard delete, no soft-delete column.** A deleted Event/User/Grant is a removed row, not a tombstoned one -
  no `IsDeleted`, no `DeletedAt`, no recovery path. Simpler schema, and nothing in the app's current scale or
  audit requirements calls for keeping deleted rows around.
- **One reusable `ConfirmDialog` component** (`Components/Shared/ConfirmDialog.razor`), opened via
  `DialogService.OpenAsync<ConfirmDialog>` the same way `Users.razor.cs` already opens `InviteDirectorDialog`.
  Takes a title, body copy, and confirm-button text/style; returns a `bool`.
- **One plain-confirm shape for every case, not type-to-confirm.** The same dialog shape covers deleting a
  `Draft` Event and deleting a `Live` one with real Directors - no extra floor (typing the Event's Name, etc.)
  for the riskier case. The consequence copy inside the dialog (Directors affected, Slug freed up) carries the
  "make them read it" job instead.
- **Delete a User refuses two specific targets**, both guard rules rather than UI-only checks: a User holding
  the Admin Role (ADR-0008 re-syncs Admin status from the config allowlist on every sign-in, so deleting an
  Admin's row doesn't actually revoke anything if their email is still listed - the allowlist, not the
  database, is the fix), and the signed-in caller's own User row (self-service deletion has its own flow,
  `Manage/DeletePersonalData.razor`, with its own consequences - it doesn't belong behind an admin action
  aimed at someone else's page).

Deleting an Event's own cascade behavior (Grants go with it) is ADR-0044's decision, not repeated here.

## Considered options

- **Soft delete with an `IsDeleted` column** - rejected: adds a query-filter concern to every resource that
  doesn't need one yet, for a recovery capability nothing has asked for.
- **Type-the-name-to-confirm, at least for a `Live`/`Past` Event** - rejected: a new interaction pattern this
  app has never needed elsewhere, for protection the consequence copy already provides.
- **Allow deleting an Admin's own row, or another Admin's, with just a warning** - rejected: the allowlist
  resync means the row would silently reappear (or the person would land as a fresh Invite target) on next
  sign-in, which reads as broken rather than as intended behavior.

## Consequences

- Every future destructive action in this app reaches for `ConfirmDialog` first, not a new bespoke dialog -
  a hand-rolled confirm here is a deviation from this ADR, not a new default.
- `UserDetail.razor`'s "Delete user" control needs the caller's own id (from `AuthenticationState`) to
  implement the self-delete guard, not just the target User's id.
