# `UserRole` grants are exposed Admin-only, and Admin-role grants stay config-owned

P2-8 (#17) needed an API resource for managing `UserRole` grants - platform-wide (Admin) or Event-scoped
(Director, and future Event-scoped roles) - so the P2-10 (#19) dashboard has something to call. ADR-0017 left
this open deliberately: `Role`/`UserRole` stayed unexposed "before any scoping rule for who can see whose
grants has been designed." The ticket's own acceptance criteria answer that question - Admin-only, 403 for
anyone else - so this ADR supersedes ADR-0017's "never exposed" clause for `UserRole` specifically. `Role`
itself stays unexposed; nothing needs it as a resource, and a grant's `roleId` attribute is resolved against
the well-known `RoleIds` constants instead.

We exposed `UserRole` at `/api/roleGrants` - not the entity's default pluralization, `/api/userRoles` -
matching ADR-0024's precedent that a public JSON:API name follows this codebase's domain vocabulary
(CONTEXT.md's Role entry: "a **grant** a User holds") over the entity's storage-shaped name.
`UserRoleResourceDefinition` gates every read and write on `RoleGrantAccessPolicy.IsAdmin`, the same
resource-definition pattern ADR-0031 established for `Event`. Only `Query`, `Post`, and `Delete` endpoints are
generated - grants are immutable; changing one is a delete followed by a create, which keeps the
duplicate-grant conflict check (mirroring `EventResourceDefinition.CheckForConflictsAsync`, pre-checking
`UserRoles`' two filtered unique indexes rather than catching the eventual `DbUpdateException`) to one code
path instead of two.

## Admin-role grants are rejected outright, not merely discouraged

The ticket's acceptance criteria ask this resource to handle a grant "whether ... platform-wide (`EventId`
null) or Event-scoped" - read literally, that includes writing an Admin grant (`RoleId = RoleIds.Admin`,
`EventId` null). We narrowed this: `/api/roleGrants` rejects both create and delete of an Admin-role grant,
for two independent reasons, either of which would be sufficient alone.

First, correctness: ADR-0008 makes the config-driven allowlist authoritative for who is an Admin, re-synced on
every login - a login promotes or demotes to match the list regardless of what the database currently says.
An Admin `UserRole` row written through this resource would be silently reverted the next time its grantee
signs in, which is a worse failure mode than refusing the write up front: a caller who gets a `201 Created`
reasonably believes the grant took effect.

Second, posture: independent of the reversion problem, this resource should not be a valid path to Admin at
all. Admin is the platform's highest-privilege role; routing its assignment through the same general-purpose
grant resource a dashboard uses for Director assignments - rather than exclusively through the config file a
deploy operator controls - is a broader surface than this ticket intends to open, even setting aside that any
such grant would not survive a login.

Reads are unrestricted for Admins: `GET /api/roleGrants` and `GET /api/roleGrants/{id}` return Admin-role rows
like any other, so the dashboard can display who currently holds Admin (as re-synced from config) alongside
Director assignments.

## A non-Admin gets 403 on every shape, including collection reads

ADR-0031 established, for `Event`, that a collection request is filtered silently (a Director's `GET
/api/events` just returns fewer rows) while a single-resource request outside the caller's access throws 403 -
because a Director's visible-Events set is a real, sometimes-non-empty state. `UserRole` has no equivalent
partial-visibility case: a non-Admin's visible set is always empty, never narrowed. Returning an empty
collection in that case would be indistinguishable from "there happen to be no grants," misreporting "you may
not see this" as a true negative - a worse lie than a 403. `UserRoleResourceDefinition.OnApplyFilter` rejects
every non-Admin request outright, collection or single alike.

This generalizes past `UserRole` specifically: **when a caller's visible set is always empty rather than
sometimes narrowed, prefer 403 over a silently-filtered collection** - the asymmetry ADR-0031 introduced
assumed the narrowed case exists; a resource that has no partial-visibility state at all should reject the
whole shape instead. Future Admin-only or otherwise all-or-nothing resources should follow this rule rather
than re-deriving it.

## Considered options

- **Allow Admin-role grants and document the reversion risk** - rejected: ships a silent-data-loss footgun
  into the P2-10 dashboard (a successful-looking `201` whose effect vanishes on the grantee's next login)
  rather than refusing it outright.
- **Make `Role` a JSON:API resource** so `UserRole.roleId` could be a `[HasOne]` relationship instead of a
  plain attribute - rejected: nothing needs `Role` exposed, and ADR-0017 already declined this; `RoleIds`
  already gives callers a stable way to resolve the id without a lookup.
- **Extend `InternalAuthorizationEndpoints` instead of adding a JSON:API resource** - rejected: that surface
  is deliberately outside `/api` and deliberately unauthenticated by role, because it is what *produces* the
  role claims a caller would otherwise need to pass its own gate - bolting Admin-only management onto it would
  be circular. It continues to coexist with `/api/roleGrants`, reading and writing the same `UserRoles` table.
- **Keep the collection/single asymmetry uniform with `Event`** (silently filter the collection instead of
  403ing it) - rejected: an always-empty filtered collection is indistinguishable from a genuinely empty one,
  which is a worse information gap than the 403 this resource's AC already requires on the single-resource
  case.

## Consequences

- A grant written through `/api/roleGrants` is subject to the same staleness ADR-0007 already documents for
  every role claim: it can take up to the internal JWT's lifetime (~5 minutes) to reach a connected caller's
  session, since Api authorizes from JWT claims alone rather than re-querying per request.
- The P2-10 dashboard cannot grant or revoke Admin through this resource - promoting an Admin remains
  exclusively a config-file change (ADR-0008). This is a real capability gap the dashboard's design needs to
  account for (e.g. surfacing Admins as read-only rows, with a pointer to how they're actually managed) rather
  than an oversight to fix later.
- `Role` remains unexposed; a client resolves `UserRole.roleId` against `VirtualLeadersGuide.Identity.Contracts.RoleIds`,
  not an `?include=`.
