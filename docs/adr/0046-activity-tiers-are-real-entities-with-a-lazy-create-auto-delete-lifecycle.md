# Tab/Sub Tab/Section/Sub Section are real entities, not string columns

Activities and InfoPages need to be grouped under Tab/Sub Tab/Section/Sub Section values that are typed into
existence while placing something and disappear once nothing references them — never separately authored or
renamed. We modeled each of the four as its own table (`Id`, `EventId`, `Name`, and for Section/Sub Section a
parent FK) rather than plain string columns on `Placement`, because "disappears automatically" is a real row
lifecycle (create-on-first-reference, hard-delete-on-last-reference), not a display artifact of grouping by
string equality — and Phase 6's Section photo needs a stable `SectionId` to attach to, which a string column
can't give it.

`Section`'s immediate parent is whichever a Placement gives it: the bare Tab when that Placement set no Sub
Tab, or that specific Sub Tab when it did — so the same name typed under two different parents produces two
distinct `Section` rows, not one shared across them (mirrors the wireframe's "independent chains" rule:
skipping Sub Tab doesn't block Section, and vice versa). `Sub Section` is always scoped to its `Section`.

A duplicate `Placement` — the identical resolved Tier path for the same Activity/InfoPage — is rejected by a
uniqueness constraint on `(PlaceableId, TabId, SubTabId, SectionId, SubSectionId)` at write time (the same
`OnWritingAsync` seam ADR-0031/ADR-0014 already use), not just flagged in the UI.

## Considered options

- **Plain string columns on `Placement`**, grouped by value equality — rejected: nothing to hard-delete when
  a group empties out (a lazy `DISTINCT` list just stops including it, but Phase 6 has nothing to attach a
  photo to), and no natural home for the "different parent, different Section" scoping rule.
- **`Section` scoped to `Tab` alone, independent of `Sub Tab`** — considered and rejected: contradicts the
  wireframe's "independent chains" framing, and would silently merge same-named Sections a Director places
  under different Sub Tabs on purpose.

## Consequences

- Four new tables (`Tabs`, `SubTabs`, `Sections`, `SubSections`), each Event-scoped, each reaped via the same
  cascading-emptiness check `Placement` removal already triggers (per P5-12's cascade rule) — walking up to
  four levels instead of two/three.
- No rename path for a Tier — typing a different name always creates a different row. This is deliberate
  (see the Phase 5 epic), not an oversight.
