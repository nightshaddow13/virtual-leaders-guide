# Internal identity-forwarding uses a short-lived JWT, not a bespoke signed header

ADR-0002 established `X-Internal-Key` as a static shared secret proving "this call came from Web," but said
nothing about forwarding *which user* is calling — a gap ADR-0006's per-request Director/Event checks need
filled. We decided Web mints a short-lived (~5 minute) JWT carrying the user's id and role claims — under
ADR-0017's `UserRole` model, that means **every** role the user holds, including Event-scoped ones (e.g.
`Director:eventId`), not just platform-wide roles — signed with a key dedicated to this purpose (separate
from `X-Internal-Key`, since the two answer different trust questions and should rotate independently); Api
validates it with standard `Microsoft.AspNetCore.Authentication.JwtBearer` middleware rather than
hand-rolled HMAC validation, and authorizes purely from those claims — it does not re-query the database per
request. Web caches the token per Blazor circuit and refreshes it lazily at point of use, rather than
minting fresh on every call or running a background refresh timer.

## Considered options

- A bespoke signed custom header, validated by hand — rejected in favor of reusing battle-tested JWT bearer
  middleware instead of writing custom crypto-validation code.
- Minting a fresh JWT on every outbound Api call — rejected as unnecessary given a token can simply be cached
  and reused within its validity window.

## Consequences

Both grants and revocations — of platform-wide roles *and* Event-scoped ones — can take up to ~5 minutes to
reach an already-connected session, since Api trusts the token's claims entirely and never re-checks the
database. A Director removed from an Event retains real write access to it until their token expires. This
is a deliberate trade-off, chosen twice over alternatives that would re-query per request or shorten the
token lifetime, in exchange for an Api that does no authorization queries and a Web UI that can render a
user's Event list straight from claims with no round-trip. (An earlier draft of this ADR assumed the
opposite — that assignment checks would be queried fresh per request via a scoping helper, making only role
revocation lag. ADR-0017 unified roles and Event assignments into one `UserRole` row, which removed the
distinction that assumption depended on.)
