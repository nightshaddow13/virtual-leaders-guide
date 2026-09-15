# Section reaping cascades into Round Robin participation

ADR-0046 hard-deletes a Section the instant nothing is placed under it — the last Activity Placement removed
from "Aquatics" reaps the `Section` row. Phase 7 (#144) introduces a second kind of reference to a Section: a
Round Robin's `RoundRobinStations` row, naming that Section as a Station, and every `Stop` that assigned a
Group to it. Neither existed when ADR-0046's cascade was designed, so left alone, reaping a Section would
leave both pointing at a deleted row.

We extend the reap to cascade into Round Robin participation: deleting a Section also deletes any
`RoundRobinStations` row naming it, and every `Stop` that named it becomes Free Time (its `SectionId` set to
null, per ADR-0063) rather than being deleted outright — a Group's Stop in that Session still exists, it's
just now unassigned, exactly as if the Round Robin had been generated with too few Stations from the start.

The reverse relationship was explicitly rejected: a Round Robin does not keep a Section alive. A Section with
zero Activity Placements is not a place to send a Group regardless of whether a Round Robin still lists it as
a Station, so Round Robin participation is never a reason to skip ADR-0046's reap.

The Placement-removal confirm dialog (P5-12, #97) gains a line naming which Round Robin, if any, is about to
lose a Station, so a Director removing an unrelated Placement isn't surprised by a rotation grid changing
underneath them.

## Considered options

- **Block the Placement removal that would reap a Section still in use by a Round Robin** — rejected:
  contradicts ADR-0046's unconditional reap-on-empty rule, and would make an Activity's last Placement
  un-removable for a reason the Activity screen gives no visibility into.
- **Round Robin participation keeps a Section alive (skip the reap)** — rejected: inverts ADR-0046's whole
  premise (a Tier's lifecycle is a pure side effect of Placement, not separately authored) and would mean a
  Section with no content still rendering as a heading somewhere, just not one anyone placed an Activity under.

## Consequences

- Reaping a Section is now a write that can touch three tables beyond `Section` itself
  (`RoundRobinStations`, `Stops`, and the confirm dialog's read of both) instead of one — implemented at the
  same `OnWritingAsync` seam ADR-0031/ADR-0014/ADR-0046 already use for write-time cascades.
- A Round Robin can end up with fewer Stations than it was generated with, purely as a side effect of Activity
  authoring on a different screen — surfaced the same way an under-provisioned generation already is (P7-12,
  #156's "Groups outnumber Stations" warning), not as an error.
