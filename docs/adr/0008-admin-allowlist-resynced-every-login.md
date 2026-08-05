---
status: sign-in mechanism amended by ADR-0019 (local ASP.NET Core Identity, not Entra ID) — re-sync-every-login decision below unchanged
---

# Admin config allowlist is re-synced on every login, not just seeded once

No prior ADR or CONTEXT.md entry addresses how the very first Admin is created, since Admin status lives only
in our own database (ADR-0006) and Entra ID has no role to check for it. We decided a config-driven list of
emails is re-checked on every Entra sign-in — the signed-in user's platform-wide `UserRole` row (the
Admin grant, per ADR-0017 — a `UserRole` with `EventId` null) is synced to match the list each time, so
adding an email promotes on next login and removing one demotes on next login.

This matches ADR-0006's "checked by app code on every request" philosophy rather than treating first login as
a one-time bootstrap ceremony, and keeps a misconfiguration always recoverable by editing config and signing
in again, since config — not the database — stays authoritative.

## Considered options

- Seed once at first login, database authoritative afterward — rejected because it would let Admin status
  silently diverge from config with no way to correct it short of direct database access.

## Consequences

The sync intentionally allows demoting every last Admin down to zero if the config list is ever emptied — no
special-casing to protect a "last Admin," since config is authoritative and always recoverable by an edit plus
a sign-in.
