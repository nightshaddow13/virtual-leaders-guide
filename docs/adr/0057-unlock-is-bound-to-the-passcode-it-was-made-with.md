# An Unlock is bound to the Passcode it was made with, via a PasscodeVersion counter — not a fingerprint

P4-2 (#72) builds the public gate ADR-0003 describes: a visitor enters an Event's Passcode and gets a signed
cookie unlocking that Event's Leaders Guide (CONTEXT.md's Unlock entry). That left an unanswered question
ADR-0003 never reached, because no gate existed yet - what happens to an already-unlocked browser when an
Admin edits the Event's Passcode. `Event.Passcode` carries `AllowChange` and the Event editor exposes it, so
mid-event rotation is possible today.

We decided the cookie records the Event's **`PasscodeVersion`** at the moment it was unlocked - a new
`int` column on `Event`, starting at `1`, incremented by 1 every time `Passcode` is written. A visitor whose
cookie's version no longer matches the Event's current version is re-locked and must enter the new Passcode.
Editing a Passcode therefore revokes every outstanding Unlock for that Event.

The version rides the same response `/e/{slug}` already fetches on every load, so this costs no extra round
trip. It must be bumped in `EventResourceDefinition.OnWritingAsync`, alongside the write that changes
`Passcode` - **not** in `Passcode`'s property setter, unlike `Name`'s trim or `Slug`'s lowercasing: EF Core
calls a property's setter when hydrating an entity from the database too, so a setter-based bump would
increment on every read.

## Considered options

- **A fingerprint of the Passcode** (a truncated hash of the normalized plaintext, carried in the cookie
  instead of a version number) - the first option considered. Rejected on two grounds surfaced while writing
  this decision up: it silently un-revokes on `P1 → P2 → P1` (both endpoints hash to the same fingerprint, so
  reverting a typo resurrects every stale cookie from before the fix), and it puts secret-derived material in
  a cookie at all, an approach that only stays safe because the cookie is Data-Protection *encrypted* rather
  than merely signed - a real but easy-to-forget dependency for a security mechanism to rest on. A plain
  version counter has neither problem: it's monotonic, so a revert is still a version bump, and its value is
  never derived from the secret it's revoking access to.
- **Rotation is a no-op for anyone already unlocked** - the cookie says only "this browser unlocked Event
  X," nothing about which Passcode. Rejected: it leaves an Admin with no revocation tool at all. The
  realistic reason to change a live Event's Passcode is that the old one leaked (posted in a unit's group
  chat, printed on the wrong handout), and a rotation that doesn't actually lock the leak out is a placebo
  the Admin will reasonably believe worked.
- **A short cookie lifetime instead, so rotation takes effect on its own** - rejected: the lifetime would have
  to be hours to be a useful revocation window, and re-entering a passcode every few hours on a phone with no
  signal is exactly the friction the gate is supposed to avoid.
- **An explicit "lock everyone out" Admin action, separate from editing the Passcode** - a real option, and
  arguably clearer. Rejected for now as a second concept to explain for a case the Passcode edit already
  implies; revisit if Admins turn out to want rotation *without* revocation.

## Consequences

An Admin fixing a typo in a live Event's Passcode boots every unit that had already unlocked, with no warning
from the data model itself. The Event editor's Passcode field should say so at the point of edit - that
warning is the mitigation this ADR deliberately chose over weakening the revocation, and it is a UI
obligation this decision creates rather than an optional nicety.

`PasscodeVersion` is an ordinary, visible column (unlike a fingerprint, nothing about it needs to stay
secret) - it can be surfaced in the Event editor or support tooling to answer "why did every unit get locked
out at 2pm," which a fingerprint-based design couldn't offer.

Nothing stores a rotation history beyond the counter itself (CONTEXT.md's Passcode entry: "one value at a
time... no rotation history"), so a re-locked visitor gets no explanation beyond the ordinary wrong-passcode
state - there is no way to tell "your passcode was rotated" apart from "you typed it wrong", and this ADR
accepts that rather than introducing history to distinguish them.
