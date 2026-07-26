# Internal identity-forwarding uses a short-lived JWT, not a bespoke signed header

ADR-0002 established `X-Internal-Key` as a static shared secret proving "this call came from Web," but said
nothing about forwarding *which user* is calling — a gap ADR-0006's per-request Director/Event checks need
filled. We decided Web mints a short-lived (~5 minute) JWT carrying the user's id and role claims, signed with
a key dedicated to this purpose (separate from `X-Internal-Key`, since the two answer different trust
questions and should rotate independently); Api validates it with standard
`Microsoft.AspNetCore.Authentication.JwtBearer` middleware rather than hand-rolled HMAC validation. Web caches
the token per Blazor circuit and refreshes it lazily at point of use, rather than minting fresh on every call
or running a background refresh timer.

## Considered options

- A bespoke signed custom header, validated by hand — rejected in favor of reusing battle-tested JWT bearer
  middleware instead of writing custom crypto-validation code.
- Minting a fresh JWT on every outbound Api call — rejected as unnecessary given a token can simply be cached
  and reused within its validity window.

## Consequences

Admin/Director role revocation can take up to ~5 minutes to reach an already-connected session — an accepted
trade-off, consistent with the caching decision. Director↔Event assignment checks are *not* baked into the
JWT — they're queried fresh against the database on every request via the scoping helper introduced alongside
the Admin/Director/Director↔Event data model, so assignment changes take effect immediately regardless of JWT
staleness.
