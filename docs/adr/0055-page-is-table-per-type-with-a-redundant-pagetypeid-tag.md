---
status: corrected and superseded in part by ADR-0059 - see below
---

# Page/InfoPage is Table-Per-Type, with a deliberately redundant PageTypeId tag column

`Page` (CONTEXT.md) is modeled EF Core Table-Per-Type: shared columns (`Id`, `EventId`, `Title`, `PageTypeId`)
live on the `Pages` table, and each subtype (`InfoPage` today) gets its own table joined on that shared `Id`.
We chose TPT over TPH (one `Pages` table with every subtype's columns, most of them nullable) so each
subtype's columns stay in its own table as more subtypes arrive, rather than accumulating a wider and wider
set of nullable columns on one shared table. This choice is independent of JsonApiDotNetCore: `Page` isn't
itself exposed as a JSON:API resource (see below), so JADNC's resource-inheritance mechanics — and the rough
edges some of its docs and issues describe around discriminator-based polymorphic resources — never come into
play here at all. If a future Page subtype turns out to share most of its columns with the others, TPH remains
open to reconsider then.

`Page.PageTypeId` is a real, if surprising, column: under plain TPT, which subtype table holds a given `Id`
already tells you the type, making a tag column look redundant. We added it anyway so an Event's Pages can be
listed with their types straight off the base `Pages` table (P5-15, #20's AC), without joining every subtype
table just to find out what each row is. Nothing at the database level keeps it truthful — a CHECK constraint
can't express "a matching row exists in InfoPages" — so it's enforced entirely by construction: `InfoPage`
exposes no public constructor other than `InfoPage.Create`, which is the one place that sets it. A future
second subtype's factory needs the same discipline, and there's nothing structural stopping that discipline
from lapsing besides code review and `PageSchemaShould`'s round-trip coverage.

`Page`/`InfoPage`/`PageType` are plain POCOs in this ticket, not `Identifiable<Guid>` - `AddJsonApi<TDbContext>`
walks every entity type in the EF model (`DbContext.Model.GetEntityTypes()`) and auto-registers any
`IIdentifiable` type it finds into the resource graph, regardless of whether it carries `[Resource]`.
Inheriting `Identifiable<Guid>` before this ticket has any authorization to protect it is a needless surface
regardless - the same reasoning that already keeps `Role` a plain POCO (ADR-0017's Consequences) - so staying
plain POCOs here is still the right call. P5-16 is the ticket that deliberately changes this.

**Correction (ADR-0059):** the sentence above originally claimed "`[Resource]` only customizes naming, it
doesn't gate exposure," and concluded from it that inheriting `Identifiable<Guid>` here would have exposed an
unauthenticated `/api/pages`/`/api/infoPages`. That's backwards. A JsonApiDotNetCore controller is generated
by its source generator specifically off `[Resource]`; graph membership by itself creates no route. P5-16
verified this directly against the JsonApiDotNetCore 5.11.0 assemblies before relying on it - see ADR-0059 for
the citation. The outcome recorded here (plain POCOs until P5-16) remains the right call for the independent
reason above; only the stated mechanism was wrong.

## Considered options

- **TPH** (one `Pages` table, `MarkdownContent` and every future subtype's columns nullable on it) - rejected
  for now: with even two subtypes this starts accumulating nullable columns that only apply to one kind of
  row. Not ruled out permanently - see above.
- **No `PageTypeId`, query the single subtype table directly** - fine today with one subtype, but querying an
  Event's Pages by type would mean unioning across every subtype table once a second one exists, and adding
  the tag column later means a backfill migration against live rows. Paying the small sync cost now avoids
  both.
- **A DB-level backstop for `PageTypeId`** (trigger, computed column) - not pursued: no portable way (ADR-0014:
  SQL Server and SQLite both) to express "a matching row exists in a specific other table" as a CHECK
  constraint, and a trigger would be the first in this codebase.

## Consequences

- Deleting an Event cascades through `Pages` to `InfoPages` (mirrors ADR-0044's reasoning for `UserRole`'s
  grant→Event cascade: a Page is meaningless once its Event is gone). Deleting a `PageType` is restricted
  while any `Page` references it, mirroring `UserRole`'s grant→`Role` foreign key.
- `PageTypeId` drift is an accepted, unenforced risk outside `InfoPage.Create` and test coverage - see above.
  This supersedes #20's own "no ADR directly" note, once PageTypeId's redundancy-by-design and the drift risk
  it carries became clear during planning.
- `Page`/`InfoPage` staying non-`Identifiable` until P5-16 means P5-16 must additionally account for `Page`
  itself: turning `InfoPage` into a resource means making its base `Page` inherit `Identifiable<Guid>`, which
  puts `Page` in the resource graph too - worth resolving explicitly when P5-16 is planned, not assumed away.
  **Resolved by ADR-0059**: `Page` is `Identifiable<Guid>` but is explicitly removed from the resource graph,
  so it carries no route regardless of the (corrected) `[Resource]`/graph-membership mechanism above.
