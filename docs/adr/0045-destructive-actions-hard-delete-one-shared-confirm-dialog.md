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
- **The consequence list is caller-supplied and conditional, not a fixed shape.** Each bullet is populated by
  the caller and omitted entirely when it doesn't apply - deleting an Event with zero Directors assigned shows
  no "Directors lose access" line at all, rather than one reading "0 directors." A caller whose consequence
  data itself comes from a fallible fetch (P2-17: an Event's Director count, from `/api/roleGrants`) degrades
  that one bullet to explanatory text ("Directors with access couldn't be loaded") instead of blocking the
  dialog from opening at all - an Admin's ability to delete a broken record shouldn't depend on a transient
  failure in data that's only advisory to begin with.
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
- A caller assembling `ConfirmDialog`'s consequence list follows the convention above - conditional bullets,
  degrade-not-block on a fetch failure - rather than inventing its own each time. #113/#114/#115 read this ADR
  for that contract, not just the dialog's parameter shape.
