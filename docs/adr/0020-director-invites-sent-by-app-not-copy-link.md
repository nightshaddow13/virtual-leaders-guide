---
status: supersedes ADR-0018 (invite delivery mechanism only — the pending-User/UserRole data shape it
  describes is unchanged)
---

# Director invites are sent by the app via email, not a copyable link

ADR-0018 decided an Admin would relay a copyable sign-in link to a new Director themselves, specifically
because the app had no email-sending capability and ADR-0005 avoided taking on an always-on paid dependency to
get one. ADR-0019 introduces Azure Communication Services Email as a dependency anyway, needed for local ASP.NET
Core Identity's password-reset flow. Once that capability exists for password reset, keeping invite delivery
manual has no remaining infrastructure justification, and it still carries the cost ADR-0018 accepted as a
trade-off: "delivering the invite link to the right person is entirely the Admin's responsibility."

We decided the app now sends the invitation email directly — an Admin invites a Director by entering their
email, and Azure Communication Services Email delivers a link to set up their local-Identity password, rather
than the app displaying a link for the Admin to copy and send however they already communicate with Directors.

## Considered options

- **Keep ADR-0018's Admin-mediated delivery, just retarget the link at local Identity's password-setup token**
  instead of an Entra sign-in URL — rejected: this was the original, more conservative plan for this round, but
  once ACS Email exists for password-reset regardless, there's no remaining reason to keep the Admin as the
  delivery mechanism, and app-sent email removes real friction (an Admin forgetting to forward a link) from the
  invite workflow.

## Consequences

- ADR-0018's core trade-off — no delivery confirmation, no resend, entirely on the Admin — is retired. The app
  can now offer a "resend" action, since it owns delivery; implementing it is left to whoever picks up P2-12
  (#43), not decided here.
- Azure Communication Services Email's cost is consumption-based and negligible at this app's scale
  (~$0.00025/email, no monthly minimum, no custom domain required).
- Issue `P2-12` (#43)'s acceptance criteria are updated to describe app-sent email instead of a displayed
  copy-link.
