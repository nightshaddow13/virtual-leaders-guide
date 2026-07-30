---
status: EntraObjectId field amended by ADR-0019 (local ASP.NET Core Identity uses a different linking field) — table shape and roles model below unchanged
---

# Users and Roles are many-to-many, scoped to an Event on the join row

ADR-0006 established that Admin/Director authorization data lives in our own database, but left the actual
shape unspecified. A literal reading of "Admin/Director" as two separate identity tables breaks down once a
person can hold more than one role: we intend to support logged-in Event participants in a future phase, and
the same person must be able to hold *different* roles on *different* Events — e.g. Director on one Event,
a Participant-style role on another. Two disjoint role tables can't express that without duplicating the
person's identity across tables.

We decided on three tables: **User** (one row per person, keyed by email — the Entra object id is null
until their first sign-in, so a person can be referenced, e.g. invited, before they ever sign in), **Role**
(Admin, Director today; more roles later), and a join, **UserRole**, carrying an optional `EventId`. A null
`EventId` is a platform-wide grant (Admin); a set `EventId` scopes the grant to that Event (Director, and
future Event-scoped roles). One person can hold multiple `UserRole` rows, each independently scoped.

`UserRole` needs a filtered unique index rather than a plain one, since SQL Server treats NULLs as equal
under a plain unique constraint — a plain index would only ever allow one no-Event row per user, blocking
nothing else, so the platform-wide and Event-scoped cases need to be indexed separately.

## Considered options

- **Separate Admin and Director tables**, matching #12's original title literally — rejected because
  ADR-0008's "demote to non-Admin" would mean deleting from one table while the person still needs a row
  elsewhere for their Director assignments, and a future Event-scoped role would need a third table.
- **A single User table with an `IsAdmin` flag, Director derived from having ≥1 Event assignment** —
  simpler for today's two roles and matches CONTEXT.md's definitions exactly, but doesn't extend to a third,
  fourth, or Event-scoped role without a schema change each time.
- **Global roles (`UserRole(UserId, RoleId)`) plus a separate `DirectorEventAssignment(UserId, EventId)`
  table** — keeps "role" and "Event assignment" as distinct concepts, but a future Event-scoped role beyond
  Director needs a second assignment table, and authorization logic has to consult two shapes instead of
  one.

## Consequences

A Role table and a three-table join are pure investment against today's actual need — there are only two
roles right now, and CONTEXT.md defines Admin as a strict superset of what a Director can do, so nothing
today requires the flexibility this buys. The trade-off is deliberate: it avoids a schema migration against
live assignment data when the first Event-scoped role beyond Director ships.

This also reshapes the "Director↔Event assignment" resource (P2-8, #17) into a general role-assignment
resource, and removes the "generic scoping helper consumed by other resources" framing from P2-3 (#12) — see
ADR-0007's amendment for why authorization now reads role claims directly instead of querying this table
per-request.
