---
status: storage mechanism superseded by ADR-0009 (encrypted-at-rest, not hashed) — access model below unchanged
---

# Passcode-gate for public Leaders Guide access, not individual accounts

Public visitors to a Leaders Guide only need read access, shared among everyone who has the code — they don't
need individual identities. We decided on a single shared Passcode per Event, checked against a per-event
secret value, granting access via a signed cookie scoped to that event (no per-visitor account, sign-up, or
profile). See ADR-0009 for why that secret is stored encrypted-at-rest rather than one-way hashed as
originally written here.

This supersedes an earlier assumption (made before the passcode-gate model was clarified) that we'd need
Microsoft Entra External ID to give public visitors individual consumer accounts. That's unnecessary complexity
for this access model — Entra is now used for Admin/Director identity only (see ADR-0006), and the public gate is
a separate, much simpler custom cookie authentication scheme.
