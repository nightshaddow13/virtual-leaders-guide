---
status: amended by P5-8 (#94) - the "tracks InfoPage's content field" consequence is now mechanical; see
  the added section below.
---

# Activity Description shares InfoPage's markdown mechanism

The wireframe's editorial public view asks for rich text in Activity's Description — "multiple paragraphs,
bold, and the occasional italic aside" — where the current model has only a plain-text field. Rather than
building a second, narrower rich-text component for Activity alongside InfoPage's existing full
markdown-authoring-and-sanitizing-render pipeline, Activity's Description reuses that same mechanism.

The wireframe's ask is a *minimum*, not a ceiling — nothing stops Activity's Description from having more
markdown surface (headings, links) than described; it just won't happen to use most of it in practice.
Building and securing one sanitizing renderer (XSS is already a named concern for InfoPage per #22) beats
building and securing two.

## Considered options

- **A separate, narrower rich-text field for Activity** (paragraphs + bold + italic only) — rejected: a
  second component to build, test, and keep secure against injection, for a strict subset of what already
  exists.

## Consequences

- `Activity.Description`'s storage type and edit/preview UX now track InfoPage's content field exactly — a
  future change to the shared markdown mechanism (e.g. adding table support) affects both.

## Amendment (P5-8, #94): the shared mechanism now includes the editor chrome

This ADR shared the markdown *parse-and-sanitize* pipeline, and P5-6 copied InfoPage's editor chrome next to
it. The "tracks InfoPage's content field exactly" consequence above therefore held only by convention - two
hand-kept copies of the Write/Preview toggle, textarea, and preview pane. P5-8 extracts that chrome into one
`MarkdownField` component used by both editors, so the consequence is now mechanical. See ADR-0073.
