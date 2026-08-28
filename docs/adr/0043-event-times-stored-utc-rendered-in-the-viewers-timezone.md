# Event Starts at/Ends at are stored UTC and rendered in each viewer's own browser timezone

P2-15 (#102) shipped as filed for the *shape* of `Event.StartsAt`/`EndsAt` (nullable, `EndsAt` requires and
must follow `StartsAt`) but expanded scope on *granularity*: issue #102's own User Story and acceptance
criteria say "start date"/"end date," and the wireframe (`Main Page Wireframes.dc.html` turn `2c`) sketched
plain date inputs with no time - but the actual requirement, confirmed twice during planning, is that these
carry a specific time of day, not just a calendar day. This ADR records the timezone model that decision
required, since the app had never needed one before.

## The model

- **Storage is UTC.** `Event.StartsAt`/`EndsAt` are `DateTimeOffset?`, but their setters normalize to UTC on
  assignment (`value?.ToUniversalTime()`) - the same shape `Name`'s trim and `Slug`'s lowercasing already
  use. Whatever offset a value arrives with, only its UTC instant survives.
- **The offset at entry comes from the entering Admin's own browser.** There is no Facility/timezone concept
  on `Event` yet (explicitly out of scope for both this story and P2-16, #103) - nothing to anchor "venue
  local time" to. An Admin typing "9:00 AM" gets exactly that instant in their own timezone, not a value
  they have to mentally convert.
- **Every viewer renders in their own browser's timezone**, not the one a value was entered with. Since the
  stored value is UTC and the entry offset is never persisted separately, this is really "renders in
  whatever timezone the viewer happens to be in" - two Directors in different zones looking at the same
  Event will see different clock times, and can legitimately see different calendar days, for the identical
  instant. `EventDateRange` (`src/VirtualLeadersGuide.Web/Events/EventDateRange.cs`) takes a
  `TimeZoneInfo`/`now` explicitly rather than reading either internally, so this conversion happens once, in
  one place, for both the dashboard grid and the Event editor.
- **"Is this the current year," used to decide whether the dashboard grid's compact format shows a year at
  all, is judged against the viewer's own local today** - consistent with the point above: pinning
  year-omission to a *different* reference frame (e.g. the server's UTC date) than the time itself renders
  in would be a second, inconsistent notion of "now" for the same displayed value. P2-16's server-side
  archiving check ("has this Event's end passed") is a separate decision that story owns for itself, not
  something this ADR settles.

## Why UTC storage, specifically

Two independent reasons converged on the same answer:

1. **Nothing needs the entry offset back.** Once every viewer's rendering goes through their own browser
   timezone (not the value's original offset), persisting anything other than the instant itself is pure
   unused bytes.
2. **It fixes a real cross-provider bug before it ships.** `CK_Events_Dates_Ordered`
   (`VirtualLeadersGuideDbContext`) compares `StartsAt`/`EndsAt` directly in SQL, and ADR-0014 requires this
   schema to also build and behave correctly on SQLite (the test provider), not just SQL Server. EF's SQLite
   provider stores `DateTimeOffset` as TEXT; comparing two values with *different* offsets in that
   representation compares them lexicographically, which does not reliably agree with comparing the instants
   they represent. Normalizing every stored value to UTC removes the mixed-offset case entirely - the CHECK
   constraint's plain `>`/`IS NULL` comparisons are correct on both engines specifically because nothing
   with a non-zero offset is ever stored.

## Why this needed the app's first JS interop

Blazor Server runs `EventEditor`/`Dashboard`'s circuits on the server - there is no way to learn what
timezone the browser is actually in without asking it directly. `BrowserTimeZoneAccessor`
(`src/VirtualLeadersGuide.Web/Time/BrowserTimeZoneAccessor.cs`) is a scoped service wrapping `IJSRuntime`,
calling a single global function in `wwwroot/js/browser-timezone.js`
(`Intl.DateTimeFormat().resolvedOptions().timeZone`) and caching the result for the circuit's lifetime.

Interop is only legal once a circuit has connected, so both pages resolve the zone in their own
`OnAfterRenderAsync(firstRender: true)`, then `StateHasChanged()`:

- `EventEditor` already disables prerendering (ADR-0036), but `OnParametersSetAsync` still runs, and runs
  *before* first render - so the form's Start/End fields are first populated against a `TimeZoneInfo.Utc`
  fallback, then re-derived from the already-loaded `EventDto` once the real zone resolves.
- `Dashboard` deliberately keeps prerendering (ADR-0036), but this costs nothing observable: per that same
  ADR, its grid rows only exist once `RadzenDataGrid.LoadData` completes, itself a circuit round trip that
  can't finish before the circuit is live - so there is no prerendered DATES cell to be wrong in the first
  place.

`BrowserTimeZoneAccessor` never throws - an unresolvable id, a disconnected circuit, or (the case bUnit's
`JSRuntimeMode.Loose` actually exercises, per ADR-0041's stated exemption) no id at all from an unconfigured
interop call, all fall back to UTC. A viewer briefly seeing UTC times is a far smaller failure than crashing
the circuit over a timezone lookup.

## Considered options

- **Store the wall-clock value with no timezone at all**, treating whatever an Admin types as the venue's
  own local time and showing it verbatim to every viewer - what most single-venue event tools do. Rejected:
  this is what Question 6 of the grilling session explicitly reversed in favor of per-viewer rendering: two
  Directors should each see the Event in their own local time, not the venue's (or, absent a venue concept,
  the entering Admin's).
- **A single fixed app-wide timezone in configuration**, used for both entry and display. Rejected for the
  same reason - a Director in a different zone from the configured one would see the council's clock, not
  their own.
- **Persist the entering Admin's original offset alongside the UTC instant**, for a possible future
  Facility/timezone feature. Rejected: nothing in this app reads it, and CONTEXT.md's own habit is not to
  carry speculative fields for a feature that isn't built - if a Facility/timezone concept arrives later,
  it's a new, deliberate design question, not an artifact this ADR should leave lying around unused.

## Consequences

- Two Admins in different timezones editing the same Event's `StartsAt`/`EndsAt` will each see it rendered
  in their own zone, not the venue's - accepted as a known limitation until a real Facility/timezone concept
  exists (out of scope for both P2-15 and P2-16). A future Facility feature is the correct fix, not a
  workaround layered on top of this model.
- No automated test exercises real cross-timezone rendering end-to-end through the UI: bUnit's
  `JSRuntimeMode.Loose` only reaches the UTC fallback path (`DashboardRenderingShould`/`EventEditorShould`),
  and Playwright's browser context is pinned to `TimezoneId = "UTC"` for determinism
  (`E2ETestBase.ContextOptions`). `EventDateRangeShould` covers the actual per-zone conversion logic
  directly, unit-style, including the same UTC instant landing on different calendar days in two different
  zones - but a manual check (switching the OS/browser timezone and reloading) remains the only way to see
  the real interop path exercised.
