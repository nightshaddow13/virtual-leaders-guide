# Projects and namespaces use the full product name, not an abbreviation

Every project in the solution (`AppHost`, `ServiceDefaults`, and later `Web`, `Api`, `Data`, test projects, etc.)
is prefixed with the full product name — `VirtualLeadersGuide.AppHost`, `VirtualLeadersGuide.Api` — rather than
an abbreviation like `VLG`. This is the first code in the repo, so the convention had no existing precedent to
follow; we picked the full name because it matches the product name already used in `README.md`/`CONTEXT.md`
exactly, with no abbreviation to introduce or explain to a future reader. Once projects exist under one prefix,
switching to the other means renaming namespaces, `.csproj` files, and every reference between them — cheap now,
increasingly disruptive the more projects get added on top.

## Considered options

- `VLG` prefix — less typing in every namespace and file, but the abbreviation appears nowhere else in the repo
  today and would need to be introduced and explained rather than read as self-evident.
