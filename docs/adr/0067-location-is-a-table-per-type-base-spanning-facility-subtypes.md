# Location is a Table-Per-Type base spanning Building/Room/Program Area/Campsite

Following Page/InfoPage's TPT precedent (ADR-0055), Location (CONTEXT.md) is a shared base type so an Activity
can hold a single optional `LocationId` FK regardless of which physical-place subtype it points at, rather
than one nullable FK per kind (`BuildingId?`/`RoomId?`/`ProgramAreaId?`/`CampsiteId?`) that grows every time a
new Location kind is added and where only one should ever be set at a time anyway. Building, Room, Program
Area, and Campsite are TPT subtypes off Location. Campsite Area is deliberately excluded from this hierarchy —
it organizes Campsites but is never itself a place an Activity can happen (see CONTEXT.md).

Schema-level details ADR-0055 had to settle for Page/InfoPage — a redundant type-tag column for cheap listing,
factory-only construction to keep it truthful, POCO vs. `Identifiable<Guid>` timing — apply the same way here
and are left to the story that builds Location's schema, which should read ADR-0055 directly rather than
have its reasoning re-litigated here.
