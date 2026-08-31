# InfoPage Placement stops at Sub Tab, and owns its Tab/Sub Tab screen exclusively

Phase 5's original plan had InfoPage share the exact same four-tier Placement shape as Activity (Tab/Sub
Tab/Section/Sub Section, via a mirrored `InfoPagePlacements` join table). We reversed that: an InfoPage's
Placement sets only Tab and, optionally, Sub Tab — never Section or Sub Section — because an InfoPage is a
whole page of content, not a heading-level item that sits *within* one alongside a list of Activities.

That forces a rendering rule: a given Tab/Sub Tab is one of two **exclusive** screens — either its
Activities, organized by their Sections/Sub Sections, or exactly **one** InfoPage. Never both, and never more
than one InfoPage sharing a slot. This is enforced at write time in both directions: placing an Activity on a
Tab/Sub Tab that already hosts an InfoPage is rejected, and placing a second InfoPage (or an Activity) on an
already-InfoPage-owned Tab/Sub Tab is rejected too.

## Considered options

- **InfoPage shares the full four-tier shape with Activity** (the original plan) — rejected once "an InfoPage
  is a whole page" was made explicit: a Section heading *inside* a page the InfoPage itself already fully
  occupies doesn't mean anything.
- **Let an InfoPage and Activities interleave on the same screen** (InfoPage content rendered above or below
  the Activity listing) — rejected as more complex for no clear benefit over one screen having one clear
  owner, and it undercuts "an InfoPage is an entire page."
- **Allow multiple InfoPages per Tab/Sub Tab** — rejected: if a Director wants more than one page's worth of
  content under one nav entry, that's what Sub Tabs are for (another screen), not stacking pages on one
  screen.

## Consequences

- `InfoPagePlacements` is schema-distinct from `ActivityPlacements` — two FK columns (`TabId`, nullable
  `SubTabId`) instead of four — not a shared polymorphic table, consistent with why Page/InfoPage itself is
  TPT (P5-15, #20).
- P5-19/P5-20/P5-21's assumption that "SortOrder is scoped within a single Tier bucket, shared between
  Activities and InfoPages placed there" no longer holds — the exclusivity rule means an Activity and an
  InfoPage can never occupy the same bucket to begin with. Those issue bodies need correcting.
- The public view (#88) needs a per-Tab/Sub-Tab render-mode branch (Activities-with-Sections vs. one
  InfoPage) rather than one uniform template.
