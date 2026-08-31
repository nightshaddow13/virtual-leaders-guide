# Tier SortOrder is reordered inline in the Activity edit page's live tree, not a dedicated screen

`Placement.SortOrder` only orders items *inside* one Tier bucket (which Activities/Sections show first under
a given Tab). Nothing ordered the Tiers themselves — which Tab shows first in the nav strip, which Section
heading shows first on a page — and the Phase 5 epic explicitly rules out a dedicated Tier management screen
(Tiers aren't separately authored, so there's nowhere else to put reordering).

We gave each Tier its own `SortOrder` column, reordered by dragging its row within the existing "Where it
appears" live tree pane on the Activity edit page (Direction C, wireframe turn 2) — the one surface that
already renders the Event's whole Tier tree, not just the current Activity's Placements. This keeps the "no
dedicated Tier management screen" rule intact: nobody *authors* a Tier there, they only reorder rows that
already exist because something references them.

## Considered options

- **No Tier SortOrder — creation order only** — considered first as the simpler default (matches "nobody
  authors a Tab list" literally), rejected once it was clear a Director needs to fix an awkward nav order
  without deleting and re-adding an in-use Tier (which the auto-delete rule wouldn't even allow while it's
  non-empty).
- **A dedicated Tier arrange screen** — rejected: reopens the "no Tier management screen" rule the epic
  deliberately closed, for a feature that fits naturally into a pane that already shows the whole tree.

## Consequences

- The Activity edit page's live-tree pane (P5-8/#94, P5-10 through P5-13) grows a second job — Placement
  management *and* Tier reordering — worth calling out since it wasn't the pane's original scope.
- The InfoPage edit page needs an analogous (simpler — Tab/Sub Tab depth only) tree pane, not just a flat
  Placement list, if Tier reordering is to be reachable from there too.
