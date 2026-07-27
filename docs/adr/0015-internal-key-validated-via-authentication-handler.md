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
