---
status: narrows ADR-0007/P2-3(#12)/P2-5(#14)'s assumption that Web queries UserRole directly against a shared
  DbContext — under this ADR it reads those grants over the same internal HTTP channel described below instead.
  Works within ADR-0002/0016 (Web stays SQL-free); ADR-0019 (local Identity) is the reason a store exists at all.
---

# Web's ASP.NET Core Identity user store forwards to Api over HTTP, not its own DbContext

ADR-0019 commits the app to local ASP.NET Core Identity, which conventionally means the project doing sign-in —
here, `Web` — owns an `IdentityDbContext` and talks to SQL directly. But ADR-0002/0016 already established `Web`
as SQL-free: internal-only `Api` is the only project with a database connection or a managed identity, and
giving `Web` its own would mean a new `CREATE USER ... FROM EXTERNAL PROVIDER` grant, a second migration set,
and walking back a boundary two prior ADRs deliberately drew.

We decided the Identity schema lives in `Api`'s existing `VirtualLeadersGuideDbContext` (now
`IdentityDbContext<ApplicationUser>`), and `Web` implements `IUserStore<ApplicationUser>` (plus
`IUserPasswordStore`, `IUserEmailStore`, `IUserSecurityStampStore`, `IUserLockoutStore`,
`IUserPhoneNumberStore` — deliberately not `IUserTwoFactorStore`, see Consequences) as thin HTTP calls to new
`/internal/identity/*` endpoints on `Api`, reusing the existing `X-Internal-Key`-authenticated internal channel
(ADR-0015) rather than opening a new one. This works because only four operations ever reach a store —
`FindById`/`FindByName`/`FindByEmail`, `Create`, `Update`, `Delete` — every other `IUserXStore` interface is
just get/set on the `ApplicationUser` instance `FindBy*` already returned. One CRUD-by-user endpoint set on
`Api` backs all of them, and `UserManager`/`SignInManager`/`DataProtectorTokenProvider` keep working unmodified
on the `Web` side — the stock Identity UI needs no rewrite, only a different store underneath it.

## Considered options

- **`Web` gets its own `IdentityDbContext` and a direct SQL connection** — the conventional Identity setup, but
  breaks ADR-0002/0016's boundary outright: a new `vlg-web-identity` managed identity, a manual database grant,
  and a second `dotnet ef migrations bundle` target alongside `Api`'s existing one.
- **Bespoke, non-store internal endpoints** (`check-password`, `issue-reset-token`, ...) instead of implementing
  `IUserStore` — rejected because it forgoes `UserManager`/`SignInManager` entirely, meaning hand-rolled password
  hashing, lockout counting, and token generation in `Web` instead of reusing Identity's tested implementation of
  all three.

## Consequences

- Password hashes, security stamps, and lockout state cross the internal Web↔Api network as plain
  `IdentityUserDto` payloads. Accepted because ADR-0002 already trusts that channel for `X-Internal-Key`; no new
  trust boundary is introduced, but this is the first time credential material specifically crosses it.
- Optimistic concurrency (`ConcurrencyStamp` compared on update, `409` on mismatch) is preserved even though the
  realistic conflict window at this app's scale is narrow (one person's own two browser tabs) — kept to match
  the framework's own contract rather than special-casing it away.
- A store-side HTTP failure is wrapped in a dedicated exception rather than mapped to "user not found," because
  `SignInManager.PasswordSignInAsync` treats a `null` `FindByEmailAsync` result as an ordinary failed login (by
  design, so it doesn't reveal whether an email exists). Without this, an `Api` outage would read to every user
  as "your password is wrong" instead of a distinguishable service error.
- No `IUserTwoFactorStore` or 2FA UI ships alongside this — TOTP-based 2FA is orthogonal to the store shape here
  and is tracked separately (issue #54), not implied by this ADR.
