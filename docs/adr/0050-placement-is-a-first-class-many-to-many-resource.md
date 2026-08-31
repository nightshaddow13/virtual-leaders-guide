# Placement is a first-class many-to-many resource, not a bare relationship

An Activity or InfoPage can appear under more than one Tab (e.g. offered both morning and afternoon), each
appearance independently ordered — so a single FK on `Activity`/`InfoPage` can't hold it. We modeled this as
`Placement`, a proper many-to-many join between a placeable thing and its resolved Tier path, carrying its
own `SortOrder`.

`Placement` is exposed as its own JSON:API resource (`/api/placements` for Activity, a separate InfoPage
equivalent per ADR-0047's schema split), not as a bare relationship, because it carries an attribute —
`SortOrder` — and JSON:API relationships carry no attributes of their own. The precedent is `UserRole` →
`/api/roleGrants` (ADR-0017): a join table promoted to a first-class resource via `[Resource(PublicName =
...)]` with a narrowed `GenerateControllerEndpoints`. `Placement` follows the same pattern and additionally
allows PATCH, since reordering a Placement is the primary reason to edit one.

`SortOrder` lives on the Placement, not on `Activity`/`InfoPage` — the same Activity placed on two Tabs has an
independent position on each, and a single ordering column on the placeable entity would make its Placements
fight over one value.

`Placement` needs its own `EventId` for Director/Event scoping, denormalized from the placeable entity's and
the Tab's Event rather than resolved through either relationship at query time — mirroring how
`UserRole.EventId` is a direct column rather than something derived by joining through `Role`.

## Considered options

- **A single nullable FK on `Activity`/`InfoPage`** pointing at one Tier — rejected: forces duplicate rows for
  anything appearing in more than one place, with no schema-level link between the duplicates.
- **`Placement` as a bare JSON:API relationship**, not its own resource — rejected: relationships carry no
  attributes, and `SortOrder` needs one.

## Consequences

- Deleting an Activity's/InfoPage's only remaining Placement is a write-time pre-check (`OnWritingAsync`, the
  same seam ADR-0031/ADR-0014 already use), not a database constraint — nothing at the schema level enforces
  "at least one Placement."
- Denormalized `EventId` is kept consistent by the same write-time pre-check that enforces every other
  Placement rule (uniqueness, exclusivity, the last-Placement guard).
