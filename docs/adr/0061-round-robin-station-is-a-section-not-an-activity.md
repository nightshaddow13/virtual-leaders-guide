# A Round Robin's Station is a Section, not an Activity, and the grid renders inside the Activities screen

Phase 7 (#144) needed to decide what one cell of a rotation grid names. The candidates were an Activity itself,
or the Section an Activity sits under (CONTEXT.md: an optional page-structure grouping, scoped to whichever
Tab or Sub Tab a Placement gives it). We chose Section: a Group is sent to a Station — "Aquatics" — for a
Session, and does whatever Activities are placed under that Section while it's there.

This is a direct reading of how the feature was framed: a named group assigned to a group of activities
defined by their Section. It also matches how camp areas actually run — one place, several things to do there
— rather than shuttling a Group between individually-scheduled Activities within a single Session.

A consequence follows for where the grid lives. Because a Station is a Section and a Section is already
scoped to a Tab/Sub Tab (ADR-0046), a Round Robin is naturally owned by that same Tab/Sub Tab, and its grid
renders as a block at the top of that page's existing Activities screen — the participating Sections' Activity
descriptions appear below it exactly as they already would. This needs **no change to ADR-0047**: the screen
is still "its Activities, organized by Section/Sub Section," just with a rotation header. It is not a third
kind of screen alongside Activities and InfoPage.

## Considered options

- **Station is an Activity** — rejected: makes Section merely a filter on which Activities are eligible, loses
  the "camp area" framing, and means a Group visiting a Station never sees the sibling Activities under the
  same Section heading.
- **Round Robin as a free-standing Event-level resource, pointing at a Section and a Tab/Sub Tab independently**
  — rejected: two things to author and keep in sync (which Tab it renders under, which Section it draws from)
  when Section's own scoping already ties it to one.
- **A third screen type on Tab/Sub Tab, alongside Activities and InfoPage** — rejected: needless change to
  ADR-0047's exclusivity rule when nothing about the grid conflicts with rendering inside the Activities screen.

## Consequences

- A Round Robin can only include Sections that already belong to its owning Tab/Sub Tab — the Station picker
  (P7-9, #153) reads the same Tier structure Placement already resolves, nothing new to query.
- At most one Round Robin per Tab/Sub Tab path (enforced by a unique constraint) — a page's rotation and its
  ordinary Activities content are the same screen, so there is only one rotation to have.
- See ADR-0064 for what happens to a Round Robin's Station when the Section backing it is reaped.
