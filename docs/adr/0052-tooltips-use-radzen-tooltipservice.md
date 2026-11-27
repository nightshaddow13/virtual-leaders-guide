---
status: amended by P2-23 (#213)
---

# Tooltips use Radzen's TooltipService

P2-18 (#113) needs this app's first tooltip: explaining, on hover, why an Event's Directors list shows a
disabled "Remove" control for a Director who separately holds Admin (ADR-0051). Nothing in the app has
used a tooltip before this - no `TooltipService` call, no `title` attribute anywhere in `Web`.

Native HTML `title` was the simpler option: no injected service, no JS interop, and a `span.GetAttribute("title")`
bUnit assertion instead of simulating a hover. `TooltipService` was chosen instead so this tooltip matches
the component library's own styling (ADR-0038's "Radzen component first" preference), and so it sets a
single precedent for every tooltip this app adds later, rather than leaving the choice to whichever ticket
happens to need the second one.

## The rule

Every tooltip in this app opens through `TooltipService.Open(...)`, never a `title` attribute.

The one piece worth recording because it isn't obvious: a **disabled** `RadzenButton` cannot own the
`MouseEnter` trigger itself. Browsers don't dispatch `mouseenter`/`mouseover` to a native `disabled` form
control, and Radzen's own `.rz-state-disabled` class additionally sets `pointer-events: none`. The pattern
is to wrap the disabled control in a plain, non-disabled element (a `<span>`) that owns the
`@onmouseenter` handler and the `ElementReference` `TooltipService.Open` anchors to - hovering the wrapper
still fires normally, since a child's `pointer-events: none` doesn't affect its parent's own hit-testing.

Inside a `@foreach`, that `ElementReference` can't be captured into one shared field - `@ref` write order
follows render order, so a single field ends up holding only the last row's reference by the time any hover
could occur. Keying a `Dictionary<TKey, ElementReference>` by a stable per-row id (here,
`EventDirectorDto.GrantId`) and binding `@ref="anchors[key]"` gives each row its own slot.

## Consequences

- `EventEditor.razor.cs` injects `TooltipService`; `RadzenTestServices.RegisterRadzenComponentsHost`
  already registers it for bUnit, alongside the other three Radzen services `<RadzenComponents>` needs.
- A future tooltip on a control that is never disabled doesn't need the wrapper-`<span>`/dictionary
  machinery above - that pattern is specifically for the disabled-control case, not a mandatory shape for
  every tooltip.

## Amendment (P2-23, #213)

The disabled-control wrapper described above is now one shared component, `DisabledTooltip`; use it rather than
writing the `<span>` again. It injects `TooltipService` and holds its own `ElementReference`, so pages no longer
inject the service for this and a row in a loop needs no dictionary.

The wrapper is also focusable, opens the tooltip on focus and closes it on blur, and carries the same text as its
accessible description. Hover alone left keyboard and screen reader users with a disabled control and no reason.
