---
status: amended by P2-5 (#14) — the "both checks compose under one AddAuthorization policy" consequence
  this ADR anticipated is now resolved; see the added section below.
---

# Internal-key header is validated via an ASP.NET Core authentication handler, not custom middleware

ADR-0002 established `X-Internal-Key` as the static shared secret proving "this call came from Web," but left
open how Api enforces it. We decided Api validates the header through a custom `AuthenticationHandler`
registered via `AddAuthentication`/`AddAuthorization` (with a fallback policy requiring an authenticated
user), rather than a bespoke middleware that short-circuits the pipeline by hand. This keeps Api on one
authentication pipeline shape rather than two: ADR-0007 already commits Api to validating P2-5's JWT via the
standard `Microsoft.AspNetCore.Authentication.JwtBearer` handler, and per P2-5 that JWT check runs *alongside*
`X-Internal-Key`, not in place of it — so both checks need to compose as ordinary authentication schemes
under one `AddAuthorization` policy. A hand-rolled middleware today would mean bolting a second, structurally
different mechanism onto that same pipeline once P2-5 lands.

## Considered options

- A plain custom middleware comparing the header to the configured value and short-circuiting with a 401 —
  simpler for this ticket in isolation, but doesn't compose with ADR-0007's JWT bearer handler; P2-5 would
  need to either wrap the middleware's result into the auth pipeline anyway or run two independent mechanisms
  side by side.

## Consequences

Api takes on `AddAuthentication`/`AddAuthorization` scaffolding now, ahead of there being any real per-user
identity to represent — the resulting `ClaimsIdentity` carries no claims, since the header only proves "this
is Web," not who the end user is (that's still P2-5's job). The fallback policy applies to every endpoint by
default, including any added later, unless explicitly opted out with `AllowAnonymous`.

## Amendment (P2-5, #14): how the JWT check composes with the fallback policy

This ADR left open exactly how `X-Internal-Key` and P2-5's JWT check would compose "under one
`AddAuthorization` policy." P2-5 resolved it as follows: the fallback policy above is **left unchanged** —
`X-Internal-Key` alone remains the floor for every endpoint, including both `/internal/identity/*` (ADR-0022)
and `/internal/authorization/*` (ADR-0024). A second, stricter named policy (`RequireInternalUser`) is added,
composing both schemes (`AddAuthenticationSchemes(InternalApiKey, InternalJwt)` + `RequireAuthenticatedUser()`
+ an assertion that a JWT-issued identity specifically is present — `RequireAuthenticatedUser()` alone would
also pass on the `X-Internal-Key` identity, since it only requires *some* listed scheme to have succeeded).
That stricter policy is applied explicitly, only to the JSON:API resource controllers
(`MapControllers().RequireAuthorization(...)`) — not as the new fallback.

Two `/internal/*` endpoint groups deliberately stay off the stricter policy, on the same trust model
ADR-0022 already accepts (anyone holding `X-Internal-Key` can read/write Identity rows directly via those
endpoints regardless): `/internal/identity/*` is called before a user is signed in (it backs `SignInManager`
itself, which has no token to present yet), and `/internal/authorization/*`'s grants-lookup is the very call
that *produces* the JWT's claims — requiring the JWT there would be circular.

**Why not make the composed policy the new fallback instead:** doing so would mean anything added to Api
later defaults to requiring a JWT it may not have, unless someone remembers to explicitly relax it back down —
inverting today's model, where the fallback is deliberately the loose one and specific surfaces opt into
strictness. Leaving the loose fallback as the default and opting the JSON:API surface into strictness keeps
that invariant intact.

Consequence: `/api/*` now requires a valid internal JWT in addition to `X-Internal-Key`; `/internal/*` does
not, and never will unless a future ticket explicitly opts a new `/internal/*` route into
`RequireInternalUser`.

A further consequence, easy to get wrong without testing it directly: a request to `/api/*` with `X-Internal-Key`
but a missing/expired/wrongly-signed JWT gets **403 Forbidden**, not 401. ASP.NET Core's authorization
middleware only challenges (401) when *no* scheme authenticated the caller at all; here `X-Internal-Key`
still succeeds on its own, so the caller has a real (if insufficient) authenticated identity, and failing the
JWT-identity assertion on top of that is an authorization failure, not an authentication one. A request with
neither header at all still gets 401, since nothing authenticated it in the first place. See
`InternalJwtAuthorizationShould` (`Api.Tests`) for both cases pinned side by side.
