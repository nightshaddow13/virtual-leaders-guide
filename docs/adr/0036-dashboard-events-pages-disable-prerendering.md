# Event editor disables prerendering; Dashboard deliberately does not

P2-9 (#18) shipped this app's first two `@rendermode InteractiveServer` pages (`Dashboard.razor`,
`EventEditor.razor` under `dashboard/events/...`). With the default prerendering left on, every click on either
page silently did nothing for a signed-in user - no navigation, no state change, no server-side log line, no
client console error, no `#blazor-error-ui` banner - while the same pages worked instantly for an anonymous
user. Radzen was not the cause (a plain native `<button @onclick>` failed identically); `[Authorize]` was not
the cause (removing it while staying signed in still failed); the custom
`IdentityRevalidatingAuthenticationStateProvider` was not the cause (disabling its DI registration in favor of
the framework default reproduced the same failure).

## The actual mechanism

Confirmed directly (not inferred) by capturing every WebSocket frame a real Playwright-driven browser exchanged
with the circuit (`Page.WebSocket`/`IWebSocketFrame`, not just server logs) around a single click on an
`InteractiveServer` page as a signed-in Admin:

- The click's `DispatchEventAsync` invocation never left the browser - zero bytes sent - even though the same
  circuit had already exchanged other frames cleanly in both directions (`OnRenderCompleted` round-tripped
  fine), so the SignalR transport itself was healthy.
- The clicked button's `outerHTML`, captured at the moment of the failing click, carried no `_bl_<guid>`
  attribute - Blazor's own marker tying a DOM node to a live circuit event handler. The *same* button, sampled
  again a couple of seconds later, had a `_bl_...` attribute and a *different* auto-generated `id` - a
  different DOM element entirely.
- A second click on the same locator, issued after that swap, dispatched normally and worked.

This matches Blazor Web App's documented prerendering model exactly: *"After the prerendered content is
quickly displayed to the user, interactive content with active event handlers are rendered, replacing any
content that was rendered previously."*
(<https://learn.microsoft.com/en-us/aspnet/core/blazor/components/prerender>). Prerendering executes the page
as a throwaway, non-interactive component instance to produce fast-first-paint HTML; the *real*, circuit-bound
instance is a separate object that only exists once the browser has opened a SignalR connection and the server
has finished constructing it (including awaiting every cascading parameter, e.g. `AuthenticationStateTask`).
Until the server sends the render batch that replaces the prerendered markup with that live instance's output,
the visible DOM is fully-formed, visually complete, and looks perfectly clickable - but it has no circuit
behind it. Any interaction landing in that window is lost with no error anywhere, because nothing is
malfunctioning: a plain static `<button>` was clicked exactly as asked.

Anonymous pages aren't immune to this window, they just close it fast enough that it's rarely lost in practice:
an anonymous `AuthenticationStateProvider.GetAuthenticationStateAsync()` resolves against an empty principal
near-instantly, while a signed-in circuit has to decode and validate the real auth cookie into a
`ClaimsPrincipal` first. That's a small delay in absolute terms, but it's enough to make an automated
(Playwright) click - and a fast human one - reliably land before the swap instead of after it, every time.

## The fix: `EventEditor.razor` only

`EventEditor.razor` disables prerendering on its own `@rendermode` declaration:

```razor
@rendermode @(new InteractiveServerRenderMode(prerender: false))
```

With prerendering off, the first thing the server sends for this route *is* the live, circuit-bound instance -
there is no intermediate static markup to race against. The trade-off is the one the docs describe: the page
shows nothing until its circuit connects and renders, instead of a fast static first paint. Given the page is
`[Authorize]`-gated and exists specifically to be typed into and submitted, that trade-off is the right one
here - it is not the default elsewhere in this app, where ADR-0034 already established static SSR.

## `Dashboard.razor` deliberately keeps prerendering on

The same fix on `Dashboard.razor` was tried first and reverted: `DashboardShould.RedirectToNoAccess_WhenThe
SignedInUserHoldsNoRoleClaim_ForDashboard` (`tests/VirtualLeadersGuide.Web.Tests`) failed against it. That
test's own header remarks already documented the mechanism it depends on: prerendering runs
`Dashboard.razor`'s `OnInitializedAsync` - including its `NavigationManager.NavigateTo("Account/NoAccess")`
call for a signed-in user with no role claim - as part of the *original HTTP request*, and Blazor turns a
`NavigateTo` during prerendering into a real HTTP redirect. `Account/Login`'s own `RedirectToLogin.razor` relies
on the identical mechanism for anonymous users. `prerender: false` deletes that request-time execution entirely
- the no-role redirect would then only happen after a live circuit connects and runs `OnInitializedAsync` a
second time, which a plain HTTP client (a non-JS crawler, or this project's own `WebApplicationFactory`-based
test) never drives. A real signed-in browser still ends up on `Account/NoAccess` either way, because Blazor's
circuit-side `NavigateTo` performs a client-side redirect once connected - only the plain-HTTP-client case
regresses.

Losing that HTTP-level redirect for a real gap in defense-in-depth (a non-interactive client hitting an
authenticated dashboard route) outweighs closing a click race that, for this specific page, is narrower than
`EventEditor.razor`'s: `Dashboard.razor`'s only prerendered-and-immediately-clickable control is its own
"+ New event" button - every row's "Edit"/"View" button only exists once `RadzenDataGrid`'s `LoadData` has
completed, which is itself a circuit-driven round trip, so by the time those buttons exist at all, the circuit
behind them has already been live for a while. None of this branch's current E2E coverage clicks "+ New event"
itself (`EventManagementScenarios` reaches `dashboard/events/new` by direct navigation), so the residual window
on that one button is accepted rather than paid for with the `DashboardShould` regression.

## Considered options

- **Speed up circuit attach** - not actionable; the delay is inherent to decoding a real auth cookie
  server-side, not a bug to optimize away, and even a fully-optimized attach leaves the same race, just
  narrower.
- **Cover the page with a client-side "not ready yet" overlay until Blazor signals interactivity** - would
  work but needs hand-rolled JS against Blazor's undocumented lifecycle events, for a problem the framework
  already has a first-class flag for.
- **`prerender: false` on both pages** - tried and reverted for `Dashboard.razor`; see above.
- **Leave prerendering on everywhere and just cross fingers about timing** - rejected: this is exactly the bug
  being fixed, and a human clicking within roughly a second of page load on `EventEditor.razor` is a realistic
  case, not just a fast automated test.

## Consequences

- A *future* `@rendermode InteractiveServer` page should default to `prerender: false` only when it doesn't
  also need a request-time `NavigateTo`-as-HTTP-redirect for an unauthorized/under-privileged visitor; when it
  does, that redirect has to be evaluated by something that runs before the interactive render (an endpoint
  filter, an `[Authorize]` policy, or middleware), not left to `OnInitializedAsync` under `prerender: false`.
- `EventManagementScenarios`/`DashboardAuthorizationScenarios` (`tests/VirtualLeadersGuide.E2E.Tests`) are the
  regression coverage for the click race itself - they drive `EventEditor.razor` through the real UI as a
  signed-in user and would have caught this before it shipped had they existed sooner.
