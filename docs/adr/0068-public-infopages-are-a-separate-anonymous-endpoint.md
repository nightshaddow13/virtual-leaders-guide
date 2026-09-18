# The public guide reads InfoPages through `/internal/public/*`, a separate anonymous endpoint, not by loosening `/api/infoPages`

P4-1 (#23) needs an unlocked visitor at `/e/{slug}` to read an Event's InfoPages. P5-16 (#21)'s acceptance
criteria said outright that "there is no public read path in this phase," and ADR-0059 scoped `/api/infoPages`
to Admin/assigned-Director via `InfoPageAccessPolicy`. We add a fourth read path rather than touch either: a
new `GET /internal/public/events/{slug}/infoPages` endpoint on `PublicGuideEndpoints`, the same
anonymous-reachable, `X-Internal-Key`-only surface P4-2 (#72) built for the Event lookup and passcode check.

The trust boundary this rests on is the same one `PublicGuideEndpoints`' own remarks already state for its
existing two endpoints: Api is internal-only ingress (ADR-0007/ADR-0015), so Web is the only possible caller of
`/internal/public/*` — an external attacker cannot reach it directly. Api itself does not check whether the
visitor has actually unlocked the Event; that check (`PasscodeUnlockCookie.IsUnlocked`) stays entirely on the
Web side, in `EventGuide.razor.cs`, exactly where the staff-bypass and Cancelled/Draft checks already live for
the Event lookup. This endpoint is therefore no more permissive than `GetEventBySlugAsync` already is — both
return data for any `Live`/`Past`/`Cancelled` Event to any caller holding the internal key, gated on nothing
but the Slug being real and the Event not being `Draft`.

## Considered options

- **Widen `InfoPageAccessPolicy` (or `/api/infoPages`'s resource definition) to also allow an anonymous
  request carrying a valid Unlock cookie** — rejected: `/api/*` is JsonApiDotNetCore's resource graph, built
  entirely around a signed-in user's internal JWT (`InternalApiClient`); teaching it to also recognize a
  cookie-based, non-Identity concept would mean threading Unlock state through a stack that assumes exactly
  one caller shape. `PublicGuideEndpoints` already exists as the answer to "an anonymous visitor needs to read
  something" — extending it is strictly less invasive than extending JsonApiDotNetCore's authorization
  pipeline.
- **A single combined endpoint returning the Event and its InfoPages together** — rejected: `GetEventBySlugAsync`
  is called before the Unlock decision is even made (it's what decides Locked vs. Unlocked), while InfoPages
  are only ever needed after that decision resolves to Unlocked. Combining them would mean fetching content the
  page may never render, and would recompute "is `Draft`" from the same lookup for a call site that doesn't
  need it.

## Consequences

- A fourth `/internal/public/*` route exists as of this ADR; `PublicGuideRoutes` carries all three route shapes
  so Api and Web can't drift, matching how the first two were already shared.
- Any future public content this guide grows (schedule, map, and eventually Activities under P5-14, #88) has a
  precedent to follow: a plain, anonymous, X-Internal-Key-gated endpoint on `PublicGuideEndpoints`, with the
  Unlock check staying in Web — not a growing set of exceptions bolted onto `/api/*`'s resource-based
  authorization.
- `InfoPageAccessPolicy` and `/api/infoPages`'s Admin/Director-only scoping (ADR-0059) are unchanged; this ADR
  does not reopen that decision, it routes around it for the one caller shape it was never meant to serve.
