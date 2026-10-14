---
status: reverses the per-page `ae-`/`ip-` split recorded in ActivityEditor.razor.css; fulfils ADR-0048's
  "the two track each other" consequence mechanically
---

# The markdown Write/Preview editor is one shared component, not a per-page copy

ADR-0048 made Activity's Description reuse InfoPage's markdown mechanism, and P5-6 (#87) honoured that by
copying InfoPage's editor chrome into `ActivityEditor` - a `RadzenSelectBar` Write/Preview toggle, a native
`<textarea @bind-value:event="oninput">`, a `MarkdownRenderer.RenderToHtml` preview surface, and a CSS-only
pane switch at 64rem. The copy was deliberate: `ActivityEditor.razor.css` recorded it as "component-scoped
(`ae-` prefix, not `ip-`) rather than shared, per ADR-0038/0040", because Blazor's CSS isolation scopes a
`.razor.css` to the one component that owns it and a rule two pages need can't live in either page's file.

P5-8 (#94) gives `ActivityEditor` an edit route and makes it the second page that authors markdown through
this chrome in earnest. The two `.razor.css` files are byte-identical modulo the prefix, and the textarea's
`oninput` binding carries a hard-won explanation (`InputTextArea` hard-codes `onchange` and silently ignores
`@bind-Value:event`) that lived only in `InfoPageEditor`'s model, with Activity's copy pointing at it. We extracted
the chrome into `Components/Shared/MarkdownField.razor`, which both editors use.

## Why this doesn't violate ADR-0040

ADR-0040's rule is that CSS only one component consumes belongs in that component's `.razor.css`. That is
exactly what happens: the pane rules now live in `MarkdownField.razor.css`, scoped to `MarkdownField`, and the
two pages no longer own any of them. The `ae-`/`ip-` prefixes existed only to keep two copies of the same
rules from being mistaken for each other; with one owner there is nothing to disambiguate, so the prefix is
dropped. ADR-0040's `.vlg-field` exemption (a rule shared by many components must stay global because
isolation would silently break the other consumers) does not apply - `MarkdownField` is the single consumer.

## Considered options

- **Leave the duplication** - rejected: nothing new would be copied, but ADR-0048's promise that a change to
  the shared mechanism "affects both" stayed true only by convention, and each copy kept its own bespoke
  rationale for the same `<textarea>` choice.
- **Extract the CSS only into `app.css`** - rejected: ADR-0040 reserves `app.css` for the token cascade, rules
  genuinely shared across many components, and document-shell chrome. One component's pane layout is none of
  those.

## Consequences

- `MarkdownField` takes `Label`, `FieldId` and a two-way `Value`. `FieldId` keeps each caller's existing
  element id (`#Description`, `#MarkdownContent`) - both are E2E locators.
- Unifying the class prefix retires `.ae-pane-preview` and `.ip-pane-preview`. **That rename is the hard-to-
  reverse part**: those strings are asserted by bUnit tests in both editors' test classes and by locators in
  `ActivityManagementScenarios` and `InfoPageManagementScenarios`, so both E2E suites must be re-run whenever
  the prefix changes again.
- The pane/preview tests move out of the two editors' test classes into `MarkdownFieldShould`, since they now
  describe the shared component rather than either page.
- A future change to the markdown editing surface (a toolbar, table support) is made once.
