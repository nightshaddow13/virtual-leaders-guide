## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for nightshaddow13/virtual-leaders-guide. See `docs/agents/issue-tracker.md`.

### Story scoping

New stories must be vertical slices (UI to DB, or DB to UI) — no API-only or other single-layer stories.
Applies to new stories only, not the existing backlog. See `docs/agents/story-scoping.md`.

### Triage labels

Default label vocabulary (needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout (CONTEXT.md + docs/adr/ at repo root). See `docs/agents/domain.md`.

### Coding standards

XML doc comment (`///`) conventions, including the interface/`<inheritdoc/>` rule, and where design rationale
belongs now that bare `//` comments don't survive. See `docs/agents/coding-standards.md` (also at
`CODING_STANDARDS.md` for tools that look there).

### Code search

Prefer `codebase-memory-mcp` over `Grep`/`Glob` for finding code definitions, implementations, and call chains. See `docs/agents/code-search.md`.
