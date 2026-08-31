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
A single thing happening at an Event (e.g. "Opening Ceremony", "Aquatics Rotation"). Has a Name and a
Description (rich text — see InfoPage's markdown mechanism, which Description shares) and belongs to exactly
one Event. A scheduled time and a Location are additions a later phase makes to this same concept — an
Activity is real and listable before it has either.
_Avoid_: ScheduleItem (describes storage shape, not the domain concept), Session (reads as a conference term)

**Page**:
A general concept for a piece of content attached to an Event's Leaders Guide. Has subtypes for different kinds
of content; only one subtype exists today.
_Avoid_: ContentPage (redundant — a page's job is content)

**InfoPage**:
A Page subtype holding free-form markdown content (About, Packing List, FAQ, etc.), authored and stored as data
through the dashboard — not checked-in `.md` files, so updates don't require a redeploy — via the same
markdown mechanism Activity's Description shares. The only Page subtype that exists today — the Page/InfoPage
split exists specifically to leave room for future subtypes (e.g. a map page, a schedule page) without
renaming the base concept later. Placed via a Placement under a Tab (optionally a Sub Tab) — never a Section
or Sub Section, since an InfoPage is a whole page, not a heading-level item within one; see Placement.
_Avoid_: ContentPage

**Tier**:
The general term for a Tab, Sub Tab, Section, or Sub Section — the four grouping levels a Placement can set.
Not separately authored; all four share one lifecycle: created inline the moment a name is typed while
placing an Activity or InfoPage, deleted the moment nothing places anything under them anymore, and never
renamable (typing a different name creates a different Tier, it doesn't rename the existing one).
_Avoid_: Grouping, Category (both were informal working terms before this glossary entry existed)

**Tab**:
A required, top-level grouping an Activity or InfoPage is placed under — the navigation unit a visitor picks
between (e.g. "Morning", "Afternoon"). Scoped to one Event. Not separately authored: created inline the
moment someone types a new name while placing something, and deleted the moment nothing places anything
under it — directly, or indirectly via a Sub Tab/Section/Sub Section that only exists under it — anymore.
_Avoid_: Category (an earlier three-tier draft of this model used Category for what is now Sub Tab)

**Sub Tab**:
An optional second-level navigation grouping, scoped to one Tab (e.g. "Round Robin" under "Morning"). Same
lazy-create, auto-delete lifecycle as Tab. Independent of Section — a Placement may set a Sub Tab with no
Section, or a Section with no Sub Tab; neither blocks the other.
_Avoid_: Category (an earlier three-tier draft of this model used Category for this tier)

**Section**:
An optional page-structure grouping for an Activity Placement — a heading wherever it's placed, not a
navigation unit. Scoped to whichever immediate parent the Placement gives it: the bare Tab, when that
Placement has no Sub Tab, or that specific Sub Tab, when it does — so the same name typed under two different
Sub Tabs (or under a Tab both with and without a Sub Tab) creates two distinct Sections, not one shared
across them. Same lazy-create, auto-delete lifecycle as Tab. **Never set on an InfoPage's Placement** — an
InfoPage is a whole page, not a heading-level item sitting within one.
_Avoid_: Sub Category (an earlier three-tier draft of this model used Sub Category for this tier)

**Sub Section**:
An optional sub-heading nested one level under a Section — the deepest tier; nothing nests under it. Scoped
to its Section, so it inherits Section's InfoPage restriction: **never set on an InfoPage's Placement**.
_Avoid_: Sub Category (an earlier three-tier draft of this model had no fourth level)

**Placement**:
An Activity's or InfoPage's appearance under a specific Tab and, optionally, a Sub Tab. An Activity's
Placement may go two levels deeper still — a Section and, under that, a Sub Section — but an InfoPage's
Placement never does, since an InfoPage is a whole page, not a heading-level item sitting within one (see
InfoPage). A Tab/Sub Tab renders as one of two exclusive screens: either its Activities (organized by their
Sections/Sub Sections) or exactly one InfoPage — never both, and never more than one InfoPage. Each Placement
carries its own SortOrder, independent of any other Placement of the same Activity/InfoPage — the same
Activity may be placed under more than one Tab (e.g. offered morning and afternoon), each ordered on its own.
The same exact path can never be set twice for the same Activity or InfoPage.
_Avoid_: Assignment, Slot (Slot reads time-based)

**Passcode**:
A single shared secret for an Event (one value at a time, editable by an Admin — no rotation history), visible
in full to an assigned Director, entered by a visitor to unlock read access to that event's Leaders Guide. Not
tied to an individual identity — anyone with the passcode gets the same access. Auto-generated (two common
words, e.g. `TigerLantern`) the moment an Event is created, so every Event has a working Passcode immediately —
never blank, unlike waiting on an Admin to set one.
_Avoid_: AccessCode, SiteCode

**Starts at / Ends at**:
When an Event runs — each a specific date *and* time, not a bare calendar day. Both optional, but Ends at
requires Starts at; "ends June 14, start unknown" isn't a real state. Ends at is always strictly after Starts
at, so a single-day Event is one whose two instants fall on the same day, not one where they're equal.
Recorded from the clock of whoever entered them and shown to each person in their own local time — there's
no venue timezone to anchor them to yet (see ADR-0043).
_Avoid_: Start date / End date (they carry a time too), Schedule, Duration, Dates (as one field — they're two)

**Status**:
An Event's position in its lifecycle: `Draft` (the default for a new or duplicated Event — not yet shown to
Directors), `Live` (published; an Admin sets this manually, independent of Starts at/Ends at), `Past`
(automatic once a `Live` Event's Ends at elapses — never applies to an Event still in `Draft`, since nothing
was ever public to conclude), or `Cancelled` (manual, only reachable from `Live`, and terminal — the record
that a gathering stopped happening, not a way to hide a `Draft` that never had an audience). `Past` and
`Cancelled` are both one-way; the only path back is duplicating the Event into a fresh `Draft` (see
Duplicate). Deleting an Event is a separate, independent action available from any Status.
_Avoid_: Archived, Active/Inactive — "archived" is not a stored value here; it's informal shorthand for
"Status is `Past` or `Cancelled`," used only to describe hiding an Event from the default dashboard list.

**Duplicate**:
An Admin action that creates a new Event by copying another's fields (Name, Starts at/Ends at, and any future
content) as a starting point, rather than typing one in from scratch. The new Event gets its own Slug and
Passcode — never the source's — and always starts in `Draft` Status, regardless of the source's. Directors
are never copied - a Grant is a decision about one specific Event.
_Avoid_: Template, Clone, Copy (as the verb - this app says Duplicate)

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
Taking one away is **Removed**, from the Event, leaving the Role itself untouched.
_Avoid_: Assignment, Permission. Revoke (as the verb for this - reserved for undoing an un-activated Invite,
a full teardown of the User itself, see Invite - a larger and different act than removing one Grant).

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
Slug, Passcode, Starts at, Ends at) are Admin-only to edit (ADR-0031). An Event can have multiple Directors, and one Director
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
