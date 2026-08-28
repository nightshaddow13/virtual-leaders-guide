# Blazor components use a code-behind, and component-specific CSS is component-scoped

Until now, every one of this app's 42 `.razor` files kept its C# in an `@code` block - zero `.razor.cs`
files existed anywhere in the repo. `EventEditor.razor` had grown to 501 lines with a 331-line `@code`
block; `Dashboard.razor`, `Users.razor`, and `UserDetail.razor` were all past 160. That C# already carries
substantial `///` `<remarks>` docs (`docs/agents/coding-standards.md`), and `<see cref="…"/>` resolution and
IntelliSense both behave worse inside `@code` than in a real `.cs` file - and none of that logic was
reachable from a unit test, because it wasn't a class anyone could name from outside the `.razor` compiler
pipeline.

Separately, ADR-0038 already pushed layout and typography off custom CSS and onto Radzen components, but
what survived that pass landed in the global `wwwroot/app.css` rather than next to the component it
belonged to. All of this app's `.razor.css` files lived under `Layout/` and `Account/Shared/` - not one
existed under `Pages/`, which is exactly where the leaked component-specific selectors came from.

## The rule

**C# lives in a code-behind.** Any `.razor` file over 40 raw physical lines total - markup and `@code`
combined, counted exactly as an editor's line-number gutter or `git diff` would show it, no adjustment for
blank lines, comments, or directives - requires a `{Component}.razor.cs` partial class. A file at 40 lines
or under may keep its `@code` inline, however that C# is shaped. This is a pure line-count threshold with no
shape-based exemption: a single-`[Parameter]` presentational component gets no special carve-out for being
"simple" - it's exempt only if it's short. One mechanical test is easier to apply consistently, in review
and in practice, than a threshold plus a list of shape-based exceptions that has to be judged case by case
and inevitably drifts.

Dependencies move with the code: a component with a code-behind declares them as `private [Inject]`
properties in the `.cs`, not `@inject` in the markup - matching, not overriding, this app's existing
`@inject`-only convention for anything staying inline under the threshold. `[Inject]` properties stay
`private`, matching how `@inject`-declared fields already behaved; there's no fixed ordering/placement
convention introduced beyond "keep them together, near the top" as an unenforced habit, since this repo has
no `.editorconfig` and no other ordering-based style rule to be consistent with.

Nested types - a private model class or enum used only by that one component, such as an `InputModel` or a
`PageState` enum - stay nested inside the code-behind partial class. They're implementation details of one
component, referenced nowhere else by name; splitting them into their own files would be a new
one-type-per-file convention this repo has no other precedent for.

`@using` directives split by what actually needs them after the split: a directive the markup still uses
directly (e.g. a type name written literally in a `TItem="…"` attribute, a `nameof(...)`, or a `@foreach`
loop variable's declared type) stays in the `.razor` file; everything else moves to the `.cs` file's own
`using` directives. The two files do not share usings - `_Imports.razor`'s global list applies only to
`.razor` files, and a `.razor.cs` file needs its own explicit `using Microsoft.AspNetCore.Components;` even
for `[Inject]`/`[Parameter]`/`[CascadingParameter]` themselves.

**Component-specific CSS lives in a `.razor.css`.** Custom CSS that only one component consumes goes in
`{Component}.razor.css`, never in `wwwroot/app.css`. `app.css` is reserved for three things: the `--vlg-*`
design-token cascade and its `--rz-*` Radzen aliasing (ADR-0034); rules genuinely shared across multiple
components (e.g. `.vlg-field`, used by 9 components); and framework/document-shell chrome that has no
component to attach to (`#blazor-error-ui`, whose markup lives in `App.razor`, the root document). This
extends ADR-0038 rather than replacing it: Radzen-component-first stays the first question for a
layout/typography/status need; this rule only governs where the CSS goes once ADR-0038 has already
concluded custom CSS is warranted. No wiring change was needed - `Components/App.razor` already references
the `VirtualLeadersGuide.Web.styles.css` isolation bundle via `@Assets[]`, so every `.razor.css` file is
already picked up.

## What's exempt

A rule genuinely shared across components stays global even after this ADR - `.vlg-field` (9 consumers) and
`.vlg-readonly-field` (`EventEditor.razor` and `UserDetail.razor` both) are not candidates for component
scoping, because Blazor's CSS isolation would scope the rule to whichever single component's `.razor.css`
it landed in, silently breaking every other consumer with no build error. `#blazor-error-ui` stays global
for the opposite reason: its markup lives in `App.razor`, the root HTML document, which isn't itself a
routable component with a natural `.razor.css` to attach to.

## The `SiteHeader.razor.css` reversal

`SiteHeader.razor.css` carried a comment claiming the theme-toggle icon-visibility rules (`.vlg-icon-sun`/
`.vlg-icon-moon`, keyed off `:root`/`[data-theme]`) belonged in `app.css`, not in the component's own
isolated stylesheet, because they're a `:root`-level concern. That reasoning doesn't hold up: verified
against the actual generated scoped-CSS bundle, Blazor's CSS isolation only appends the scope attribute to
the *rightmost* compound selector in each rule - `[data-theme="dark"] .vlg-icon-moon` compiles to
`[data-theme="dark"] .vlg-icon-moon[b-xyz]`, leaving the `[data-theme="dark"]` ancestor clause untouched.
Since `data-theme` is set on `document.documentElement` (`<html>`, matching `:root`) by both `App.razor`'s
inline pre-paint script and `wwwroot/js/theme-toggle.js`, the ancestor selector still matches regardless of
scope. The stated reason for keeping the rule global was authorial adjacency to the `--vlg-*` token cascade,
not a real technical constraint - and splitting one button's styling across two files is exactly the smell
this ADR's CSS rule targets. The rule now lives in `SiteHeader.razor.css`, alongside the `.vlg-theme-toggle`
button styling it was previously split from.

## A duplication fixed along the way

Three `InputModel`s already in scope for this migration - `SetupAccount`, `ResetPassword`, and
`Manage/ChangePassword` - repeated an identical `[StringLength(100, ErrorMessage: "…", MinimumLength: 6)]`
stack on their password field, verbatim. Since every file carrying it was already being touched, it was
fixed here rather than filed separately: a new `PasswordLengthAttribute` (`Identity/PasswordLengthAttribute.cs`)
replaces just the `[StringLength]` piece, bounds baked in rather than taken as constructor arguments (all
three call sites used identical bounds, and there's no current need for one to differ). `[Required]` stays
a separate, explicit attribute on each field, unchanged - the new attribute doesn't subsume it, so a
password field's required-ness is validated the same way as every other required field in the app. Named
"Length", not "Strength", because it only enforces a size range, not complexity - digit/uppercase/symbol
rules are already enforced server-side by ASP.NET Identity's own `PasswordOptions` inside
`UserManager.AddPasswordAsync`/`ChangePasswordAsync`, and a "Strength" name would have overclaimed what this
attribute checks.

## Considered options

- **An absolute ban on `@code`, with no size exemption** - rejected: a code-behind for a 5-line
  single-`[Parameter]` component (`PrimarySubmitButton.razor`) costs more in file-navigation than it buys in
  separation, and this repo has several such components.
- **An absolute ban with named, shape-based exemptions** (e.g. "a component with only one `[Parameter]` and
  no lifecycle method may stay inline regardless of size") - rejected in favor of the pure threshold: a
  shape-based list is exactly the kind of judgment call that has to be litigated in review and drifts over
  time, where a line count is unambiguous and needs no interpretation.
- **Subsuming `[Required]` into `PasswordLengthAttribute`** - rejected: technically equivalent today (an
  empty string already fails the length check), but it would conflate two distinct validation *reasons*
  into one, departing from how every other required field in the app is validated, for no benefit tied to
  this migration's actual goal.

## Consequences

- 13 `.razor` files gained a sibling `.razor.cs`: `EventEditor`, `Dashboard`, `Users`, `UserDetail`,
  `InviteDirectorDialog`, `SetupAccount`, `Login`, `Manage/ChangePassword`, `ResetPassword`,
  `Manage/DeletePersonalData`, `Manage/Index`, `ForgotPassword`, `Manage/PersonalData`.
- `wwwroot/app.css` dropped from 413 to 339 lines. Two long-dead orphans were also removed in the same
  pass: `.blazor-error-boundary` (no `<ErrorBoundary>` exists anywhere in this app) and two classes with no
  matching rule at all (`vlg-signout-form`, `vlg-footer-sep`).
- `Pages/EventEditor.razor.css` is this repo's first `.razor.css` under `Pages/`.
- This is purely a structural/organizational change - no page's rendered behavior or CSS output changed,
  other than the `PasswordLengthAttribute` dedup, which preserves the prior error message and bounds
  exactly.
