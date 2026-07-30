---
status: superseded by ADR-0020 (app-sent invite email, not copy-link)
---

# Director invites are a copyable sign-in link, not a sent email

An Admin needs to grant Event access to someone who has never signed in before — ADR-0017's User table
supports this (a row can exist, keyed by email, before its Entra object id is ever populated), but nothing
in the app actually gets that person from "invited" to "signed in." The obvious mechanism is an invitation
email, but the repo has no email-sending capability today — no SMTP, no SendGrid, no Azure Communication
Services, verified across `src/`, `infra/`, and `docs/` — and ADR-0005 chose every other service in this
stack specifically to minimize cost, scaling every compute and data service to zero when idle.

We decided an Admin invites a Director by entering their email, which creates the pending `User` +
`UserRole` rows immediately, and the app then displays a copyable sign-in link for the Admin to send
however they already communicate with Directors. This mirrors an existing pattern in the domain: ADR-0009
already has the Passcode handed to visitors "out-of-band (email, printed handout)" by a human, rather than
the app sending it. The invited row activates — gains its Entra object id — on that person's first sign-in.

## Considered options

- **A real transactional email provider** (Azure Communication Services, SendGrid) — rejected for now as the
  stack's first always-on, non-free external dependency, requiring a verified sender domain and a new
  managed secret alongside `X-Internal-Key` and the internal JWT signing key. Worth revisiting if invite
  volume or UX friction ever makes copy-link genuinely painful.
- **Require the Director to sign in once, unprompted, before an Admin can find and assign them** — rejected
  because it inverts the workflow an Admin actually needs (grant access to a known person ahead of their
  first visit) and gives the Admin no way to initiate the process.

## Consequences

Delivering the invite link to the right person is entirely the Admin's responsibility — the app has no way
to confirm it reached anyone, or resend it, beyond re-displaying the same link. This is an accepted
trade-off for avoiding an email dependency at this stack's scale; revisit if that changes.
