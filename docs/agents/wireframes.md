# Wireframes: required before planning new/changed UI

A story that adds a new screen, adds a new UI element to an existing screen, or visibly changes an
existing screen's layout must have a wireframe **before an implementation plan is written for it**.
"Wireframe" is intentionally loose here — a hand-drawn sketch, an ASCII mockup, a Figma link, an
annotated screenshot of the existing page. What matters is that the shape of the UI was decided by
the user, not inferred by the agent while planning.

**Do not invent the layout yourself and proceed.** If a story needing one shows up without a
wireframe attached (in the issue body, as a comment, or as a file the user points to), stop and ask
the user for one via `AskUserQuestion` before finalizing the plan. Offer to sketch an ASCII mockup as
a starting point if that helps them react to something concrete, but the accepted version — theirs,
or your draft they've confirmed — is what the plan should build against.

## Scope: new or changed UI only

This does **not** apply to:

- Test coverage (E2E/unit/integration) for UI that already exists and isn't changing.
- Backend-only, data-model-only, or infra/config stories.
- Copy-only tweaks to existing markup where the layout itself isn't moving.

If a story is a vertical slice per `docs/agents/story-scoping.md` but its UI layer is unchanged from
what's already shipped (e.g. new tests, a new field with an obvious placement in an existing form
pattern the app already uses everywhere), use judgment — the bar is "does a human need to decide what
this looks like," not "does this story touch a `.razor` file at all."
