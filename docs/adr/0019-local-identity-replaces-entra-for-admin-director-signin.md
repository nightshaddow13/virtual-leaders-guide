---
status: amends ADR-0006 (identity-provider clause only, authorization-in-our-database clause below unchanged); amends ADR-0017 (EntraObjectId field only)
---

# Local ASP.NET Core Identity replaces Entra ID for Admin/Director sign-in

ADR-0006 decided Entra ID would be used for identity only, with Admin/Director authorization modeled entirely
in our own database. That authorization decision is unchanged. But it also committed to Entra as the *identity*
provider, which meant every Admin and Director — including volunteer Directors with no relationship to our own
Entra tenant — would need a Microsoft account and would sign in through an Entra App Registration (the original
P2-1/P2-2). Provisioning that registration surfaced a real, if minor, cost: a multi-tenant registration (needed
because Directors aren't members of our tenant) shows an "unverified publisher" indicator to organizational
sign-ins. On its own that cost was too small to justify a change — it's a cosmetic label on low-privilege OIDC
scopes, not a blocking warning.

The larger reason is that depending on Entra ties every Director's ability to sign in to a Microsoft account
they may or may not already have configured the way we need, for an app whose actual account population is
small and self-contained. We decided to own the full account lifecycle locally instead: ASP.NET Core Identity
with email + password sign-in, cookie authentication, and app-owned password reset — accepting the new
dependencies that requires (password storage, and an email-sending capability, see ADR-0020) in exchange for
not requiring any particular external account type from a Director.

## Considered options

- **Entra ID, multi-tenant (`AzureADandPersonalMicrosoftAccounts`)** — the original P2-1/P2-2 plan. Rejected:
  once the team decided an email-sending dependency was acceptable anyway (see ADR-0020), Entra had no
  remaining advantage worth requiring Directors to have a Microsoft account.
- **Entra ID, single-tenant with B2B guest invites** — avoids the unverified-publisher label entirely, but
  requires a human to manually invite every Director into our tenant before P2-12's in-app invite even works.
  Rejected as a permanent, recurring operational cost for every future Director, which is worse than the cost
  it avoids.

## Consequences

- A new dependency on Azure Communication Services Email is introduced, needed for password-reset delivery
  (see ADR-0020, which also uses it for invite delivery).
- ADR-0017's `User.EntraObjectId` field is repurposed as a local-Identity credential/user-id field, nullable
  until account setup — the same pending/activation shape ADR-0017 already established, just keyed on a
  different provider's identifier.
- **No MFA today.** ASP.NET Core Identity supports TOTP-based 2FA, but only with enrollment/recovery UI we
  haven't built. Entra would have given org users MFA via their own conditional access policies at no cost to
  us. This is a deliberate, accepted trade-off given Admin is currently a single person and Director is a
  low-privilege, per-Event role — revisit if the org's account population or risk profile grows.
- Password storage and reset flows are now owned by the app rather than delegated to a third party — a new
  secret/data surface (password hashes) alongside the app's existing managed secrets.
- Issues `P2-1` and `P2-2` (#1, #11) are rewritten in place to reflect this mechanism; their position in the
  Phase 2 dependency graph is unchanged.
