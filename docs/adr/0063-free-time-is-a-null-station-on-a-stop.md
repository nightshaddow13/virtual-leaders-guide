# Free Time is a null Station on a Stop, not a sentinel row

A Round Robin's generator (#144) pads its Station list to match the Group count whenever Groups outnumber
Stations, so every Group gets a Stop in every Session even when there's nowhere left to send it. We represent
that padding as a `Stop` row with a null `SectionId`, rather than creating a real "Free Time" Section, Activity,
or other sentinel entity that the padding points at.

A sentinel row would have to exist without ever being a real Station a Director chose — it can't appear in the
Section picker (P7-9, #153), can't be reaped like an ordinary Section (ADR-0046), and can't carry a
Description the way a real Activity's does. Modeling "no Station" as the absence of a foreign key, rather than
as a specific row standing in for absence, avoids building special-casing into every place that already has to
handle nullable-and-optional (the Section picker, the reaping cascade, the public grid's rendering) just to
keep a fake entity out of them.

## Considered options

- **A real `FreeTime` Section (or Activity) instance, seeded once and pointed at by every overflow Stop** —
  rejected: a Section that must never be deleted, must never appear in an ordinary Placement picker, and
  exists purely as a foreign-key target is a special case dressed as a normal row; every consumer of Section
  data would need to know to filter it out.
- **A separate `StopKind` enum (`Station`/`FreeTime`) alongside a nullable `SectionId`** — rejected as
  redundant: the two would always agree (`FreeTime` iff `SectionId` is null), so the enum can only ever drift
  out of sync with the column it duplicates.

## Consequences

- Rendering a Stop is a single null check, not a lookup against a sentinel id.
- A Stop's Free Time state needs no migration or seed data — it falls out of the column already being
  nullable, since `Stop.SectionId` has to be optional for this to work at all.
- See ADR-0064: reaping a Section turns every Stop that named it into Free Time the same way — by nulling
  `SectionId`, not by repointing it at anything.
