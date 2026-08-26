# Icon-only buttons are the standard for table row actions

P2-9 (#18) shipped the Events grid's action column as a single icon-only `RadzenButton` (`edit`/`visibility`,
Material Symbols names) rather than a text button ("Edit"/"View"). This was a direct, explicit ask from the
user while reviewing the first cut of the grid, which used a text button: *"Lets also change the Edit button
to an icon instead of the word. We will want to use icons on table rows over words as a standard."*

That last sentence is the part worth recording: it isn't a one-off style choice for this column, it's a
standing rule for every table row-action button this app builds from here on, including columns that don't
exist yet (Pages, Activities). Leaving that rule as an inline comment next to this one column would make it
invisible to whoever builds the next one.

## The rule

A table row action is rendered as an icon-only `RadzenButton`, not a text button:

```razor
<RadzenButton Icon="edit" ButtonStyle="ButtonStyle.Light" aria-label="Edit" Click="..." />
```

- `Icon` takes a [Material Symbols](https://fonts.google.com/icons) name, matching `RadzenButton`'s own
  convention for the parameter.
- `aria-label` is required on every icon-only row action — there's no visible text for assistive tech to
  fall back on.
- The column hosting it needs enough fixed width for the rendered button plus real breathing room on both
  sides, not just the button's own footprint — `RadzenDataGrid`'s fixed table layout (`rz-grid-table-fixed`)
  can't grow a column to fit content that doesn't fit, it clips instead.

## Considered options

- **Text buttons ("Edit", "View")** — what P2-9 shipped first; rejected per the user's own review feedback
  above. A word column also costs more horizontal width than an icon does, which matters more as more
  row-action columns (Pages, Activities) stack up in the same grid.
- **Icon buttons with a text label alongside (icon + word)** — not requested; adds width back without the
  discoverability an icon-only pattern is trying to buy in the first place.

## Consequences

- Every future table row-action column follows this pattern; a text button in that position is a deviation
  from this ADR, not a new default.
- `aria-label` is the only substitute for the missing visible text — omitting it on a new row action is a
  regression against this ADR, not just a general accessibility nit.
