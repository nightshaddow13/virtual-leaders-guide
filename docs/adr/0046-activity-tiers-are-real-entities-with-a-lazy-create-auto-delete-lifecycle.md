---
status: amended by P5-11 (#96) — Section's parent FK shape
---

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

## Amendment (P5-11, #96)

This ADR left `Section`'s "parent FK" at a conceptual level - "whichever a Placement gives it." P5-11 settles
the concrete column shape: two nullable FKs, `ParentTabId` and `ParentSubTabId`, with a
`CK_Sections_ExactlyOneParent` CHECK constraint enforcing exactly one is non-null - the same
exactly-one-of-two-nullables pattern `Event`'s own `CK_Events_Dates_Ordered` constraint already uses for
`StartsAt`/`EndsAt` (`VirtualLeadersGuideDbContext.BuildDatesOrderedCheckSql`), so this introduces no new EF
Core or JsonApiDotNetCore concept. `SubSection` needed no such decision - it's always scoped to its `Section`
by a single required `SectionId`.

The alternative considered and rejected was a single `ParentId` plus a `ParentKind` discriminator column
("Tab" or "SubTab") naming which table it points into. That was rejected because EF Core can't express a
real foreign key from one column into two different target tables, so `ParentId` would carry no DB-level
referential integrity at all for this relationship - and a discriminator-column idiom appears nowhere else
in this schema, which otherwise always prefers a real, enforced FK (see `ConfigureFacilities`'s
`Facility→FacilityType` FK for the schema's general posture on lookup references).
