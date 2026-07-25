# Admin/Director authorization data lives in our own database, not Entra ID

The platform has two roles: Admin (platform-level, can create Events and edit any of them) and Director
(access granted to specific Events via a many-to-many assignment). Director access is fine-grained and changes
often — a poor fit for Entra ID app roles or security groups, which are coarser and slower to manage for this kind
of per-resource grant.

We decided Microsoft Entra ID is used for identity only (establishing who a person is), while Admin/Director
status and the Director↔Event assignment are modeled entirely in our own database and checked by the app on every
request. Entra is never asked "is this user an Admin" — our own data is authoritative for that.

## Consequences

Authorization logic lives in application code and our schema, not in Entra configuration — this is more visible
and testable, but means we don't get Entra's built-in role-management UI, and we own building an admin UI for
managing the Director↔Event assignments ourselves.
