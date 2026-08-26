---
status: supersedes CONTEXT.md's original Director entry ("not a platform-wide Role") and the Role entry's
  Role/grant equivalence — ADR-0017's Role/UserRole table shape is otherwise unchanged
---

# A Director holds the Role unscoped; Event access is a separate Grant

P2-12 (#43) needs to invite someone as a Director before any Event exists to assign them to — the whole
point of inviting ahead of time is that Event assignment can happen later, possibly much later. That
forced a question CONTEXT.md's original Director entry answered the wrong way for this case: it defined
Director as "not a platform-wide Role," i.e. a `UserRole` row without an `EventId` was assumed meaningless
for anyone but Admin.

We decided a Director genuinely can hold the Role with `EventId = null` — called **unscoped** — and that
this is a normal, permanent state, not a waiting room. `EventAccessPolicy` and `EventAccessView` already
treat an unscoped Director claim as granting nothing (both only match
`roleName == Director && eventId is Guid`), so this costs no authorization change; it makes explicit and
intentional a state the code already handled correctly by accident.

Making that state real forces **Role** and **Grant** apart as separate concepts, where CONTEXT.md
previously treated "a Role" and "a `UserRole` row" as the same thing. Holding the **Role** is a standing,
one-per-person fact, established once by Invite and never touched again. A **Grant** is the separate,
zero-or-more, Event-scoped row that actually extends a held Role's authority onto one Event. Admin doesn't
participate in this split at all — holding Admin already *is* full access to every Event, always, so Admin
never has Grants; the split exists only for Director (and future Event-scoped roles).

One consequence: Event assignment is one-directional. An Admin adds a Director to an Event from the
Event's own page, never the reverse, and the Event page's picker only ever lists Users who already hold
the Director Role — it cannot promote an arbitrary User (Admin or otherwise) to Director on the spot. The
Role is established exactly one way: Invite.

## Considered options

- **Require an Event to invite a Director** (the literal reading of issue #43's first AC) — rejected: it
  contradicts the actual need (invite ahead of Event creation) and would need reversing the moment a
  second use case showed up.
- **Treat "invited, no Event yet" as no `UserRole` row at all** — i.e. the Role isn't held until the first
  Grant exists — rejected: it can't represent Jo, a Director with zero Events, as anything other than "not
  a Director," which is the exact state this ticket needs to allow. It would also make "is this User a
  Director" require inferring a fact from its absence rather than reading a row.
- **A transient placeholder row, deleted the moment the first Grant is written, recreated if Grants ever
  drop back to zero** — rejected in favor of a permanent row: it adds create/delete bookkeeping to every
  future Grant and revoke path for no behavioral benefit, since the permanent row is otherwise never
  touched.
- **Let the Event page promote any User to Director on the spot** (write the Role and a Grant together) —
  rejected: it opens a question this ticket doesn't need to answer (should an Admin be Director-able?) and
  gives "how did this person become a Director" two divergent code paths to keep consistent.

## Consequences

- `UserRole`, `/api/roleGrants`, `RoleGrantDto`, `ApiRoleGrantClient.CreateGrantAsync`, and
  `GrantCreationOutcome` all predate this split and now store/name both Role rows (Admin's, and a
  Director's unscoped row) and Grants (Event-scoped rows) under "grant"-flavored names. Left as-is for
  P2-12 — renaming touches P2-8's already-shipped resource, tests, and the E2E `IdentityApiClient` — with a
  follow-up ticket filed to bring the code in line with the vocabulary.
- Revoking an un-activated Invite is a full teardown: it deletes the unscoped Role row and the
  `ApplicationUser` row together, and cascades over any Grants already assigned before the person ever set
  a password (assigning Events to a not-yet-activated Director is possible, since the Role is held from
  the moment of Invite — password-set only gates the ability to sign in, not what's held).
- `RoleIds.Director` combined with `EventId = null` is reserved for the unscoped-Role state and is not
  itself a Grant; code that enumerates a Director's "access" must filter it out the same way
  `EventAccessPolicy`/`EventAccessView` already do.
