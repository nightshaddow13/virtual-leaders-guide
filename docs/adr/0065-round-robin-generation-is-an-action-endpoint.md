# Rotation generation is a server-side action endpoint, not a JSON:API write

Generating a Round Robin's grid (P7-12, #156) replaces every `Stop` row for that Round Robin in one operation —
up to `SessionCount × GroupCount` rows, computed by the algorithm in the Phase 7 issue (#144), from a request
that carries no row data of its own beyond "generate this Round Robin." We expose it as a plain `MapPost`
minimal-API action (`POST /api/roundRobins/{id}/generate`), sitting alongside the resource graph rather than
as a JSON:API write to `/api/stops`, following the precedent `PublicGuideEndpoints`
(`POST /api/guide/{slug}/passcode`, ADR-0004's REST generation layer coexisting with hand-written endpoints
for shapes JsonApiDotNetCore doesn't fit) already set for a non-CRUD action.

JSON:API has no bulk-replace primitive that fits "delete every existing Stop for this Round Robin and insert a
freshly computed set" as one atomic unit — a client would have to `GET` the existing Stops, `DELETE` each,
then `POST` each new one, none of it transactional at the framework level, all of it exposing the client to a
grid that's half-regenerated if any step fails partway. A single action endpoint wraps the whole replace in one
`DbContext` transaction and returns the finished grid, matching P7-14 (#158)'s requirement that a rejected
re-generate (the admin cancels ADR-0045's confirm) leaves the prior grid completely untouched.

## Considered options

- **A JSON:API `PATCH` to the `RoundRobin` resource with a computed side effect** — rejected: JSON:API resource
  semantics describe writing the fields sent in the request body, not "recompute and replace a related
  collection nobody sent."
- **Client-side generation, POSTing the computed Stops as ordinary JSON:API creates** — rejected: puts the
  generation algorithm in two places (it already has to exist for validation/preview), and loses the one-
  transaction guarantee P7-14 depends on.

## Consequences

- The generation algorithm lives once, server-side, and is unit-testable directly (pure function of Sections,
  Groups, and `SessionCount` — see the Phase 7 issue) without a database or an HTTP round trip.
- This endpoint needs its own authorization check, mirroring `InfoPageAccessPolicy`'s pattern (ADR-0059) rather
  than inheriting whatever `RoundRobin`'s JSON:API resource definition enforces for ordinary writes, since it
  sits outside that resource's generated controller entirely.
