# `vlg-web` authenticates to Azure Communication Services Email via connection string, not managed identity

`vlg-web` currently holds no Azure managed identity — ADR-0016's managed-identity preference is scoped to
SQL access on `vlg-api`; `vlg-web` never talks to SQL directly and has no other data-plane dependency to date.
Provisioning a managed identity for `vlg-web` solely to authenticate outbound email would be new
infrastructure for a single call path. The ACS connection string is instead handed to `vlg-web` as a
Container Apps secret, the same shape as `internal-api-key`.

## Consequences

- The connection string is a bearer credential: anyone who reads it can send email as this app until it's
  rotated (`az communication regenerate-key`), unlike a managed identity's short-lived tokens.
- If `vlg-web` ever needs a managed identity for another reason, revisit this decision — once the identity
  exists anyway, consolidating ACS auth onto it removes a standing secret at near-zero marginal cost.
