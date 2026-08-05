# Code search

This repo is indexed by `codebase-memory-mcp`. Use it first for anything code-shaped.

## When to use which tool

- **Finding a definition, implementation, or call chain** — `search_graph` (functions/classes/routes by
  name or natural-language query), `get_code_snippet` (exact source once you have a qualified name from
  `search_graph`), `trace_path` (call/data-flow chains), `search_code` (graph-augmented grep, when you need
  text matches enriched with containing-function context). These beat plain `Grep`/`Glob` for code: they
  understand structure (functions, classes, routes) rather than just lines, and rank results accordingly.
- **`get_architecture`** — project structure, when you need the shape of the codebase rather than one symbol.
- **Text, configs, markdown, and other non-code files** — `Grep`/`Glob` still, since the graph only indexes
  code.
- **Always `Read` a file before editing it**, even after `get_code_snippet` — the snippet tool is for locating
  and understanding, not a substitute for reading the file you're about to change.

## If the project isn't indexed

Run `index_repository` first. Check status with `index_status` if a query returns unexpectedly little.
