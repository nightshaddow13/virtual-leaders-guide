# Unlock is a purpose-isolated Data-Protection cookie, not a second ASP.NET Core authentication scheme

ADR-0003 called for granting a visitor "access via a signed cookie scoped to that event... a separate, much
simpler custom cookie authentication scheme," but P4-2 (#72) is the first ticket to actually build that gate,
and ADR-0003 never settled *how* "custom cookie authentication scheme" should be implemented in ASP.NET Core
terms. Identity's own sign-in already uses a real `AddAuthentication().AddIdentityCookies()` scheme producing
a `ClaimsPrincipal` (P2-2, #11) - the literal reading of ADR-0003's wording would be a second such scheme,
authenticated separately, alongside it.

We decided against that. `PasscodeUnlockCookie` is a plain cookie whose value is protected with a
purpose-isolated `IDataProtector` (`"VirtualLeadersGuide.Web.PublicGuide.Unlock"`) - read and written directly
by the two public pages, with no `AddAuthentication` scheme, no `ClaimsPrincipal`, and nothing registered in
the authentication/authorization pipeline at all. `IsUnlocked`/`Unlock` are ordinary method calls a page's
code-behind makes, the same shape `Identity.InviteTokenProvider` already established for a second
Data-Protection-purposed token living alongside Identity's own machinery, and `Components.Account.IdentityRedirectManager`'s
`StatusCookieBuilder` already established for a plain, hand-appended cookie.

An Unlock authorizes exactly one thing - whether `/e/{slug}` renders its Locked or Unlocked body for one
Event - and never appears in a `ClaimsPrincipal`, is never checked by `[Authorize]`, and never interacts with
Identity's own sign-in state. A visitor who unlocks a guide is not "signed in" by any definition this app
otherwise uses; conflating the two by building the gate as an authentication scheme would make every future
`[Authorize]`/`AuthorizeView` read as if it might mean either concept, when in practice it never means Unlock
(the shell's `<AuthorizeView>` in `SiteHeader` shows the signed-in staff strip; whether a visitor is unlocked
plays no part in that check and is never meant to).

## Considered options

- **A real second authentication scheme** (`AddScheme<UnlockAuthenticationOptions, UnlockAuthenticationHandler>`,
  composed into `MapGroup`/`RequireAuthorization` on the guide pages) - rejected: a `ClaimsPrincipal` and a
  registered scheme are machinery for something that needs to compose with other authorization policies or
  show up in `HttpContext.User`; nothing about Unlock does either. It would also invite a scheme name choice
  (`"Unlock"`? `"Guide"`?) with no natural home in `IdentityConstants`, and every future reader would have to
  learn a second meaning for "authenticated" in this app.
- **One cookie listing every Event a visitor has unlocked** (a single `vlg_guide` cookie holding a set of
  Event ids/versions) instead of one cookie per Event - rejected: unbounded growth for a visitor who unlocks
  many Events over time (a repeat camper, a sibling unit), and losing the ability to let each Event's Unlock
  expire independently (ADR-0057's date-tracking expiry) without re-parsing and rewriting the whole set on
  every visit to any Event.

## Consequences

`PasscodeUnlockCookie` needs no `builder.Services.AddAuthentication(...)` changes at all - just
`AddScoped<PasscodeUnlockCookie>()`, reusing the `IDataProtectionProvider` Web already registers
(`AddWebDataProtection`, P2-2). If a future ticket ever needs Unlock to interact with `[Authorize]` or appear
in `HttpContext.User` (unlikely, given CONTEXT.md's Unlock entry - it's deliberately unrelated to signing in),
that's a new decision to make then, not an extension of this one.
