---
status: supersedes ADR-0017's table-count decision (User/Credential collapse onto one row) — the Role/UserRole shape ADR-0017 decided is otherwise unchanged
---

# The domain User table collapses onto ApplicationUser; there is no separate Credential row

ADR-0017 designed a `User` table distinct from any credential row specifically because Entra ID owned
identity at the time: our database needed a person a role grant could reference independent of, and
possibly before, whatever external account they'd eventually sign in with. ADR-0019 then replaced Entra
with local ASP.NET Core Identity, meaning credentials — `ApplicationUser : IdentityUser` — are now our own
table too. ADR-0019's amendment only repointed `User.EntraObjectId` at the new provider's identifier; it
didn't revisit whether a separate `User` table was still justified once the credential row it was meant to
exist independently of became one of our own tables.

It isn't. Building `User` as ADR-0017 describes it would produce two rows per person, both keyed by email —
`AspNetUsers` (already has `Email`/`NormalizedEmail`) and a new `Users` table duplicating the same columns —
with nothing keeping them in sync. We decided `ApplicationUser` **is** the person: `UserRole.UserId`
references `ApplicationUser.Id` (a string) directly, and there is no `User.CredentialId`/`EntraObjectId`
field to repurpose, because there is no second row for it to link to. "Credential" survives only as informal
language for the password-related columns on that one row (`PasswordHash`, `SecurityStamp`, lockout state),
not as a separate concept with its own row — CONTEXT.md's `User` entry is rewritten accordingly.

`ApplicationUser` is exposed at `/api/users` (JSON:API) so P2-8 (#17) and P2-10 (#19) have a person resource
to relate `UserRole` to and pick from, respectively — something the old `AspNetUsers`-is-never-a-resource
design (ADR-0022) didn't provide and this collapse now needs. Exposure is attribute-gated: only properties
explicitly marked `[Attr]` (`Email`, `DisplayName`) are ever serialized or accepted by JsonApiDotNetCore,
regardless of what other columns the underlying row has, and the resource is read-only
(`GenerateControllerEndpoints = JsonApiEndpoints.Query`) — credential columns (`PasswordHash`,
`SecurityStamp`, `ConcurrencyStamp`, lockout state) stay unmarked and are therefore unreachable through
`/api`, the same containment guarantee ADR-0022 already relied on for the equivalent Identity tables.
`ApplicationUser` implements `IIdentifiable<string>` directly rather than deriving from JsonApiDotNetCore's
`Identifiable<T>` convenience base class, since C# can't stack that class on top of `IdentityUser`.

## Considered options

- **Build the `User` table as ADR-0017 literally describes it** — the original plan for this ticket.
  Rejected once it became clear this meant two email-keyed rows per person with no mechanism syncing them,
  which ADR-0017's own justification (a credential-independent person row) no longer requires now that
  credentials are local.
- **Keep a `User` table, but without an email column — sourced from `ApplicationUser` via the credential
  link instead** — removes the duplication, but a pending invite (P2-12, #43) has no `Credential` yet by
  definition, which would leave that `User` row with no key to look it up by, breaking the invite flow's
  core requirement.
- **Table-split a second, credential-free entity type over `AspNetUsers`** instead of attribute-gating
  `ApplicationUser` directly — considered for `/api/users` exposure specifically, since it would put
  credential columns on a type that isn't a JSON:API resource at all, rather than a resource that merely
  doesn't declare `[Attr]` on them. Rejected as unnecessary: JsonApiDotNetCore never serializes a property
  without `[Attr]` regardless of resource-level access, so the containment guarantee is identical, and
  attribute-gating avoids an EF Core shared-column mapping between two entity types for the same table.

## Consequences

- `UserRole.UserId` (ADR-0017) is `string`, matching `IdentityUser<string>.Id`, not the `Guid` a standalone
  domain `User` table would have used.
- `InternalAuthorizationEndpoints` (this ticket, P2-3/#12) owns only grant CRUD
  (`/internal/authorization/users/{id}/grants`) — person CRUD already lives in `InternalIdentityEndpoints`
  (ADR-0022), and there is no separate domain-`User` CRUD surface to duplicate it for.
- `IdentityEntitiesAreNotJsonApiResourcesShould`'s `/api/users` case (previously asserting 404, from when no
  entity there implemented `IIdentifiable`) moves to a positive assertion (`UsersResourceShould`) that
  `/api/users` returns 200 with only `email`/`displayName`, and that write attempts are rejected.
- `ApplicationUser` gains a `DisplayName` property (on both `Api`'s and `Web`'s copies, and
  `IdentityUserDto`) — not asked for by ADR-0017 or CONTEXT.md's original `User` entry, but needed now that
  a JSON:API person resource exists for P2-10's picker to eventually render something better than a bare
  email address. Unset by anything in this ticket.
