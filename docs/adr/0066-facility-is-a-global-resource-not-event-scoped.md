# Facility is a global, reusable resource — not scoped to a single Event

Unlike Activity, InfoPage, and Tier — every one of which keys off `EventId` — a Facility (CONTEXT.md) is
created and persists independently of any Event, then assigned to as many Events as reuse it, many-to-many.
We chose this over an Event-scoped copy (re-entering a camp's Buildings/Rooms/Program Areas/Campsites for
every new Event held there) so a Facility's physical layout is entered once and improved over time, not
re-created — and left to drift — on each reuse. This is a deliberate break from every other domain concept
built so far, worth recording so a future reader doesn't assume a `Facility` row carries an `EventId` the way
everything else in this codebase does.

Consequence: the Director-Grant model (ADR-0035), which extends a Role's authority onto one specific Event,
doesn't apply to a resource with no owning Event. Who may create/edit a Facility is left to whichever story
implements it — likely Admin-only, mirroring how platform-wide, non-Event-scoped resources (Users, Roles) are
already handled — but that's a decision for that story, not this one.
