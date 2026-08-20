# Radzen Blazor (free `material` theme) replaces Bootstrap as the UI component library

P2-11 (#33) needed a component library for the dashboard work ahead (P2-9, P2-10, P3-3) instead of each
ticket deciding UI from scratch. ADR-0001 described the stack but never named a UI component library —
Bootstrap was only ever a `dotnet new blazor` template default, not a decision. We picked Radzen.Blazor
(MIT): the richest free component set (DataGrid w/ EF `IQueryable` support, Scheduler, Charts) among
MudBlazor / Microsoft FluentUI Blazor / Ant Design Blazor, closest to a Telerik/Kendo-style toolkit.

The ticket's original note assumed the `material3` theme. That's a **premium** Radzen Blazor Pro theme, not
in the free MIT package (`ThemeService.Themes.All` marks it `Premium = true`; there is no `material3*.scss` in
the package's `themes/`; `ThemeService.Embedded` excludes it, so `<RadzenTheme Theme="material3" />` resolves
to `css/material3-base.css` in this app's own `wwwroot`, which doesn't exist and 404s). We use the free
**`material`** base theme instead.

Colors, radius, and card elevation come from this project's own design-system tokens — not from Radzen's
`material` defaults, and not from hex values eyeballed off a wireframe sketch. The Claude Design project
**"Virtual Leaders Guide"** (`projectId c79dcd66-8d26-45ac-ae9e-7c09add75d91`, linked from issue #72) defines
a maintained `--vlg-*` OKLCH token system in its `styles.css` — full light/dark theme pairs, an explicit
"where each hue is allowed" table, and stated accessibility targets (body text ≥12:1, accent-on-surface
≥4.5:1). Its wireframe file's later turn, explicitly labeled *"2b is the direction,"* re-skins the shell
layout with those tokens: thin borders, `--vlg-radius`/`--vlg-radius-lg` corners, and the **soft**
`--vlg-shadow` — not the hard 3px-offset shadow the wireframe's earlier, pre-color-system turn used. `--rz-*`
tokens are aliased onto `--vlg-*` (`--rz-primary` → `--vlg-moss`, `--rz-danger` → `--vlg-clay`, etc.) rather
than hardcoded, so future palette or dark-mode changes stay a one-file edit. The signed-in strip specifically
uses `--vlg-pine`, which `styles.css` documents for exactly that role ("dark panels, staff/admin chrome,
section headers") — the wireframe's own 1e/1f mockups render that strip in an undocumented one-off tan that
matches no token and wasn't carried into the later revision; treated as a stale pre-color-system placeholder,
not a deliberate deviation. Typography stays Radzen's stock `material` scale — the token system doesn't define
one, and this ticket doesn't need to invent one to match it.

A second, unrelated Design project (**"Website main page wireframe"**, `ec317c69-...`) surfaced during
planning holds only the earlier, pre-color-system wireframe turn and doesn't appear in `list_projects` (i.e.
it isn't a tracked design-system project) — it isn't authoritative and shouldn't be cited going forward.

## The shell must work under both static SSR and an interactive circuit — and it does not use `<RadzenComponents />`

The app renders 100% static SSR today (Interactive Server is registered but nothing opts in) and
`MainLayout` is shared with the `[ExcludeFromInteractiveRouting]` Account pages, which depend on the plain
`method="post"` + `[CascadingParameter] HttpContext` flow for cookie sign-in. That rules out any shell
component whose behavior is C#-driven open/close state — `RadzenProfileMenu` and `RadzenMenu` render
permanently collapsed and unopenable under SSR, and `RadzenButton`'s `@onclick` is silently inert. The shell
(`SiteHeader`/`SignedInContextStrip`/`SignOutForm`/`SiteFooter`) uses only components that render as plain
markup with no JS-interop-driven state: `RadzenCard`, `RadzenStack`, `RadzenRow`/`RadzenColumn`, `RadzenText`,
`RadzenLink`, and `RadzenAlert` with `AllowClose="false"`. Navigation and sign-out stay real `<a>`/`NavLink`
elements and a real `<form method="post">`, never Radzen's click-driven equivalents.

This isn't a temporary simplification to be "upgraded" later — it's permanent for the shell specifically,
because P2-9/P2-10 will add pages that *do* need an interactive circuit (a `RadzenDataGrid` with paging, a
`DialogService`-driven form) via `@rendermode InteractiveServer` on those pages, while `MainLayout` keeps
wrapping both kinds of page at once. `AddRadzenComponents()` registers `DialogService` et al. as services, but
we deliberately don't add a `<RadzenComponents />` host to `MainLayout` — doing so with `@rendermode` would
open a SignalR circuit on every page including `Account/Login`, and without one it's four inert empty divs.
**A future page that needs `DialogService` should declare its own `<RadzenComponents
@rendermode="InteractiveServer" />` next to its own `@rendermode` declaration** — an interactive island inside
the statically-rendered shell — rather than promoting the whole layout to interactive.

## Light/dark toggle is vanilla JS, not Blazor

`styles.css`'s `--vlg-*` tokens ship full light/dark pairs, applied via `[data-theme]`/`prefers-color-scheme`
(above). A manual toggle followed from that for free on the CSS side, but needs *something* to flip
`<html data-theme>` and remember the choice across visits — and per this ADR's static-SSR constraint, that
can't be a Blazor `@onclick`. `wwwroot/js/theme-toggle.js` is plain vanilla JS: a `click` listener on
`document` (not the button itself, so it survives Blazor's enhanced navigation replacing `<body>`) that
flips `data-theme` and writes `localStorage`. `App.razor`'s `<head>` has a small blocking inline script that
reads that same key before first paint, to avoid a flash of the wrong theme on load — the one place this
ADR's "no interactivity" shell still needed *some* JS, deliberately outside Blazor's rendering model rather
than bending it.

## Considered options

- **Radzen Blazor Pro, for `material3`** — rejected: a paid subscription and a vendored theme CSS blob in
  `wwwroot/css/` are exactly what removing Bootstrap was trying to get away from.
- **MudBlazor / Microsoft FluentUI Blazor / Ant Design Blazor** — rejected in favor of Radzen for component
  breadth (DataGrid, Scheduler, Charts) closest to what the dashboard work needs.
- **`<RadzenComponents />` in `MainLayout`, globally interactive** — rejected: forces a SignalR circuit onto
  every page, including the static-SSR Account sign-in flow, for a capability most pages don't use.

## Consequences

- Any new shell-level chrome (nav, header, footer) must be checked against the static-SSR constraint before
  reaching for a Radzen component with click-driven state — it's easy to reach for `RadzenMenu` or
  `RadzenProfileMenu` since they look like the obvious fit, and both are silently broken here.
- P2-9/P2-10 pages that need `DialogService` add their own
  `<RadzenComponents @rendermode="InteractiveServer" />`, scoped to themselves, not to the shared layout.
- `styles.css` has no `--vlg-on-pine` contrast token; `SignedInContextStrip.razor.css` overrides Radzen's own
  `.rz-button.rz-base.rz-shade-default` color with `!important` for this reason — verified visually (a real
  Playwright screenshot, not just a build) that `--vlg-on-primary` reads correctly against `--vlg-pine` in
  both themes before shipping.
- The design project is a live, user-owned artifact and can change after this ADR is written — re-read
  `styles.css` via `DesignSync` before trusting a token value quoted elsewhere (including this ADR) verbatim.
