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
A single shared secret for an Event (one value at a time, editable by an Admin — no rotation history), visible
in full to an assigned Director, entered by a visitor to unlock read access to that event's Leaders Guide. Not
tied to an individual identity — anyone with the passcode gets the same access. Auto-generated (two common
words, e.g. `TigerLantern`) the moment an Event is created, so every Event has a working Passcode immediately —
never blank, unlike waiting on an Admin to set one.
_Avoid_: AccessCode, SiteCode

**User**:
A person with a row in ASP.NET Core Identity's own table (`ApplicationUser`/`AspNetUsers`), keyed by email.
Created either on their first sign-in, or earlier by an Admin's Invite — a User can exist, and hold Roles,
before they've ever set a password (their row's `PasswordHash` is null until then; informally, "Credential"
refers to that password-related state on the same row, not a separate one — see ADR-0024).
_Avoid_: Account, Identity, Credential (as a separate concept — it's columns on this row, not another row)

**Role**:
A standing capability a User holds, independent of any Event — Admin or Director. Holding a Role is a fact
about the person, established once and never re-derived from anything else they hold. For Admin, holding
the Role already *is* full access to every Event, always — there's no further step. For Director, holding
the Role by itself grants nothing (see Director's _unscoped_ state) — actual access to a particular Event
comes from a separate Grant, layered on top. One User holds at most one Role-row per Role. Stored as a
`UserRole` row — the same table Grants use (see ADR-0035 for why the table/endpoint name predates this
split).
_Avoid_: Permission, Group. "Staff" is UI copy only (e.g. the public site's "Staff sign in" affordance,
meaning "anyone holding the Admin or Director Role") — it is not a Role and must not appear as one in code,
an API shape, or a claim.

**Grant**:
An Event-scoped extension of a held Role's authority onto one specific Event. Applies only to Director (and
future Event-scoped roles) — never Admin, whose Role already covers every Event with no separate step. A
Director may hold any number of Grants, including zero. Made by an Admin, from the Event being granted —
never the reverse — and only to a User who already holds the Director Role (see Invite for how that's
established). Stored as a `UserRole` row with a non-null `EventId`, exposed to Admins at `/api/roleGrants`.
_Avoid_: Assignment, Permission.

**Admin**:
A platform-wide Role: holding it already is full access — can create, edit, and delete any Event's content,
always, regardless of Director assignment, with no separate Grant step (see Role). A superset of what a
Director can do.
_Avoid_: Owner, SuperUser

**Admin allowlist**:
A config-driven list of emails, re-synced on every sign-in (ADR-0008): a listed email's User is promoted to
Admin on their next login, an unlisted email's User is demoted on theirs. Config is authoritative, not the
database — emptying the list demotes every Admin, with no special-casing to protect a "last Admin."
_Avoid_: Whitelist, seed list, bootstrap list (this isn't a one-time seed — it's re-checked every login)

**Director**:
A Role a User holds independent of any Event (see Role). Holding it alone grants nothing — a Director with
no Grants at all is **unscoped**, a normal and permanent state, not a waiting room (e.g. someone invited but
never assigned to an Event). Read access to a specific Event comes only from a Grant for that Event; what a
Director may *write* is decided per resource, not inherited from the Role itself — Event details (Name,
Slug, Passcode) are Admin-only to edit (ADR-0031). An Event can have multiple Directors, and one Director
can hold Grants for multiple Events. The Role itself is established exactly one way, by Invite; Grants are
added afterwards, from the Event, never the reverse (ADR-0035).
_Avoid_: Organizer, Admin (these are for the platform-level or generic role — Director is specifically
event-scoped). Platform-wide (reserved for Admin's Role, which behaves oppositely to an unscoped Director —
holding it grants everything, not nothing).

**Invite**:
An Admin creates a User by email before that person has ever signed in, and grants them the Director Role
immediately, unscoped (see Director) — delivered via an app-sent email with a password-setup link, not a
copyable link for the Admin to relay. Setting a password doesn't change what Role or Grants the person
holds; it only lets them sign in to exercise them. An Admin can revoke an un-activated Invite outright,
deleting the User and anything (the Role, any Grants assigned before activation) attached to it.
_Avoid_: Invitation link, copy-link invite

**Leaders Guide**:
The public-facing destination for an Event — what a visitor reaches after entering the Passcode. Contains the
Event's Activity schedule, map, and InfoPages. One Event has exactly one Leaders Guide. This is the platform's
namesake concept.
_Avoid_: Site, Microsite, Public Page
