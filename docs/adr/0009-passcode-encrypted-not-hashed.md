---
status: supersedes ADR-0003 (storage mechanism only)
---

# Passcode is stored encrypted-at-rest, not one-way hashed

ADR-0003 specified the Passcode is "checked against a per-event hash," implying one-way hashing. But
CONTEXT.md describes Passcode as something an Admin/Director sets and needs to communicate to visitors
out-of-band (email, printed handout) — a need a true one-way hash can't serve, since the current value could
never be re-displayed. We decided to store the Passcode encrypted-at-rest (reversible) rather than one-way
hashed, so the Admin/Director dashboard can always display an Event's current Passcode.

This explicitly contradicts ADR-0003's literal wording on the storage mechanism — the shared-secret-cookie
access model ADR-0003 describes is otherwise unchanged.

Encryption uses ASP.NET Core's built-in Data Protection API rather than hand-rolled AES: key generation,
rotation, and storage (Azure Key Vault as the key ring provider in production) come for free, versus a third
manually-managed secret alongside `X-Internal-Key` and the internal JWT signing key (ADR-0007).

## Considered options

- True one-way hash, faithful to ADR-0003's original wording, with the UI never re-displaying the current
  Passcode (only allowing it to be reset) — rejected as a poor fit for a passcode whose entire job is being
  handed out to visitors after the fact.
- Manual AES encryption with a dedicated key — rejected in favor of the Data Protection API's built-in key
  management, avoiding a third hand-managed secret.

## Consequences

Encryption-at-rest still protects the Passcode against a raw database dump, while remaining recoverable by
the app itself for display purposes — a different security property than a one-way hash, and a deliberate
trade-off given the domain's actual usage pattern.
