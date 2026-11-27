# Public guide Tab/Sub Tab selection is a name-keyed query string on the static SSR page

P5-14 (#88) gives the unlocked guide at `/e/{slug}` Tabs and Sub Tabs. The ticket asks for "client-side
navigation on one page, not separate routes", but the page is static SSR on purpose (ADR-0034, ADR-0058): it
has no circuit. Each entry is therefore a real link back to the same `@page`, `e/{slug}?tab=…&sub=…`, with no
new route and no circuit.

The Tabs sit in the site's top bar (P2-26, #241), not in strips on the page. A Tab with Sub Tabs is a native
`<details>` dropdown, and on a narrow screen all the Tabs fold into one menu. The page hands them to the bar
through a Blazor section. `<details>` does not close on an outside click or Escape, so `wwwroot/js/menus.js`
does that, and also when focus leaves.

So the bar stays one row however many Tabs an Event has, the ones that don't fit go under a "More" dropdown.
The server cannot measure widths, so it counts label characters (`GuideMenu.ShownCount`). It is an estimate: an
unusually wide set of labels can still wrap to a second row. Measuring in the browser was rejected as more
script than the case is worth.

The query string carries Tier **names**, not ids, matched case-insensitively within the parent. A Tier is never
renamed (ADR-0046), and a shared link keeps working after a Tier is reaped and typed again, which would give it
a new id.

A name is meant to be unique per parent, but only the write path's lookup-before-insert keeps it so: the
database has no unique index, and two placements saved at once could each create the same Tier. The guide
therefore treats same-named siblings as one Tier, merging their content, as the dashboard's tree already does.

Which screen a link lands on:

- A bare Tab is its own screen (ADR-0047). When the Tab has Sub Tabs and its bare screen has content, its
  dropdown starts with an entry labelled with the Tab's own name (`?tab=Day`, no `sub`).
- A Tab's default screen is its bare screen when that has content (or it has no Sub Tabs), otherwise its first
  Sub Tab. The guide's default is the first Tab.
- A link that no longer resolves - an unknown Tab or Sub Tab, or `?tab=` alone asking for a bare screen that is
  now empty beside Sub Tabs - falls back to the nearest default and shows "That part of the guide isn't
  available anymore."

## Considered options

- **Render every screen and toggle with CSS** (radio/`:target`) - rejected: a much heavier page, and two levels
  of selection state in CSS is fiddly and hard to link to.
- **An `InteractiveServer` island for the unlocked body** - rejected: it needs a live circuit, which is fragile
  on camp cell signal, and it undoes the page's static-SSR stance.
- **Tier ids in the query string** - rejected: unreadable, and a shared link silently breaks when a Tier is
  recreated.

## Consequences

- Picking a Tab is a full page load. The menus turn off Blazor's enhanced navigation, which would keep the
  previous screen's scroll position.
- Visitors hold on to shared links, so the `tab`/`sub` parameter names are a contract.
