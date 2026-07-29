# Virtual Leaders Guide

A platform for managing event Leaders Guides: an Admin/Director dashboard for creating and editing Events, each
of which has its own passcode-gated public Leaders Guide with a schedule, a map, and info pages.

## Language

**Event**:
The top-level thing an Admin creates one per gathering. Generic on purpose — covers any themed gathering
(splash-themed, spooky-themed, etc.), not just "-oree"-style scouting events. The specific flavor/theme is just
Event.Name, not a distinct concept. Name is a display label and may repeat across Events (see Slug for the
part of an Event that must be unique).
_Avoid_: Camporee, Jamboree, Gathering (these are event *themes*, not the canonical noun)

**Slug**:
The URL-safe identifier for an Event's public route (`yourdomain.com/e/{slug}`). Auto-derived from Name but
editable, and unique across all Events — unlike Name, which is just a display label and may repeat.
_Avoid_: Route, Path, Key

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

**User**:
A person with a row in our own database, keyed by email. Created either on their first Entra sign-in, or
earlier by an Admin's Invite — a User can exist, and hold Roles, before they've ever signed in. Distinct
from their Entra identity, which a User's row links to only once they've signed in at least once.
_Avoid_: Account, Identity

**Role**:
A grant a User holds — either platform-wide (Admin) or scoped to a specific Event (Director, and future
Event-scoped roles). One User may hold several Roles at once, scoped to different Events. Role/assignment
data lives in our own database, not in Entra ID (Entra is identity only).
_Avoid_: Permission, Group

**Admin**:
A platform-wide Role: can create, edit, and delete any Event's content, regardless of Director assignment —
a superset of what a Director can do.
_Avoid_: Owner, SuperUser

**Director**:
A Role granting read and edit access (not create or delete) to one or more specific Events — not a
platform-wide Role. An Event can have multiple Directors, and one Director can be granted access to multiple
Events. The grant is made by an Admin, either by choosing an existing User or via Invite.
_Avoid_: Organizer, Admin (these are for the platform-level or generic role — Director is specifically
event-scoped)

**Invite**:
A Role grant an Admin creates for someone by email before that person has ever signed in, paired with a
copyable sign-in link the Admin sends however they already communicate with Directors (no email is sent by
the app itself). Resolves into an active grant the moment that person completes their first Entra sign-in.
_Avoid_: Invitation email

**Leaders Guide**:
The public-facing destination for an Event — what a visitor reaches after entering the Passcode. Contains the
Event's Activity schedule, map, and InfoPages. One Event has exactly one Leaders Guide. This is the platform's
namesake concept.
_Avoid_: Site, Microsite, Public Page
