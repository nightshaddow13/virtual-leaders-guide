# A Placement SortOrder PATCH is a move-to-position, and the server renumbers its bucket densely

P5-13 (#98) reorders the Placements that share one exact Tier path. `PATCH /api/placements/{id}` reads the
supplied `sortOrder` as a **target position**, not a value to store. The server clamps it to `[0, n-1]`,
moves the row there, and renumbers every Placement at that path to 0..n-1.
`ActivityPlacementResourceDefinition.OnWritingAsync` does all of it inside JsonApiDotNetCore's one
`SaveChangesAsync`, extending ADR-0072's one-write-path rule to reorder.

Rows that already share a value are ordered by Activity name, ignoring case, which is the order the tree
showed them in before this story. A new Placement is assigned `Max(SortOrder) + 1`, not the row count, so it
lands last even after a removal has left a gap.

## Considered options

- **Store the value as sent, and have the client swap neighbours** - rejected: a move becomes two PATCHes
  with no transaction between them, and rows that already share a value make the swap ambiguous.

## Consequences

- The stored value is not necessarily the value sent. P5-12's Undo relies on this to put a restored row back
  at its exact position.
- A move also repairs any gaps or duplicate values already in that bucket.
- Other paths, including other Placements of the same Activity, are never touched (ADR-0050).
