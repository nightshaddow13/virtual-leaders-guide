# Story scoping: vertical slices only

A story (issue/ticket) must be a **vertical slice** — a narrow but complete path from the UI down to the
database, or from the database up to the UI. It has to be demoable and testable end-to-end on its own: a
person can click through the app (or, for now, drive it via the E2E harness) and see the capability work,
not just confirm a schema exists or an endpoint responds.

**Do not scope a new story as API-only, data-model-only, or any other single-layer slice.** A story that
only adds a JsonApiDotNetCore resource with no UI able to reach it, or only adds an EF Core entity with
nothing above it querying it yet, is a horizontal slice and should not be created going forward — fold the
layers it needs (schema + API + UI, at minimum) into one story instead, sized so the whole slice still fits
in a single agent session per `to-tickets`' tracer-bullet sizing rule.

This mirrors the `to-tickets` skill's existing vertical-slice rule (`.agents/skills/to-tickets/SKILL.md`) —
this file makes it a hard requirement for this repo specifically, not just a default a skill happens to
suggest.

## Scope: new stories only

This applies **only to stories created from now on**. Do not retroactively split, merge, or re-scope
existing open issues to fit this rule — the current backlog's phase-numbered layer tickets (e.g. a data-model
ticket, a separate API-resource ticket, and a separate dashboard-UI ticket for the same feature) stay exactly
as filed and get finished as filed.
