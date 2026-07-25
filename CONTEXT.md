# Virtual Leaders Guide

A platform for managing event Leaders Guides: an Admin/Director dashboard for creating and editing Events, each
of which has its own passcode-gated public Leaders Guide with a schedule, a map, and info pages.

## Language

**Event**:
The top-level thing an Admin creates one per gathering. Generic on purpose — covers any themed gathering
(splash-themed, spooky-themed, etc.), not just "-oree"-style scouting events. The specific flavor/theme is just
Event.Name, not a distinct concept.
_Avoid_: Camporee, Jamboree, Gathering (these are event *themes*, not the canonical noun)

**Activity**:
A single scheduled thing happening at an Event (e.g. "Opening Ceremony, 9:00am–9:30am"). Belongs to exactly one
Event.
_Avoid_: ScheduleItem (describes storage shape, not the domain concept), Session (reads as a conference term)

**Page**:
A general concept for a piece of content attached to an Event's Leaders Guide. Has subtypes for different kinds
of content; only one subtype exists today.
_Avoid_: ContentPage (redundant — a page's job is content)

**InfoPage**:
A Page subtype holding free-form markdown content (About, Packing List, FAQ, etc.), authored and stored as data
through the dashboard — not checked-in `.md` files, so updates don't require a redeploy. The only Page subtype
that exists today — the Page/InfoPage split exists specifically to leave room for future subtypes (e.g. a map
page, a schedule page) without renaming the base concept later.
_Avoid_: ContentPage

**Passcode**:
A single shared secret for an Event (one value at a time, editable by an Admin/Director — no rotation history),
entered by a visitor to unlock read access to that event's Leaders Guide. Not tied to an individual identity —
anyone with the passcode gets the same access.
_Avoid_: AccessCode, SiteCode

**Admin**:
A platform-level role that can create new Events and can edit any Event's content — a superset of what a
Director can do. Role/assignment data lives in our own database, not in Entra ID (Entra is identity only).
_Avoid_: Owner, SuperUser

**Director**:
A role granted access to edit one or more specific Events (not a platform-wide role). An Event can have multiple
Directors, and one Director can be granted access to multiple Events — the grant is a many-to-many assignment
between Director and Event, made by an Admin.
_Avoid_: Organizer, Admin (these are for the platform-level or generic role — Director is specifically
event-scoped)

**Leaders Guide**:
The public-facing destination for an Event — what a visitor reaches after entering the Passcode. Contains the
Event's Activity schedule, map, and InfoPages. One Event has exactly one Leaders Guide. This is the platform's
namesake concept.
_Avoid_: Site, Microsite, Public Page
