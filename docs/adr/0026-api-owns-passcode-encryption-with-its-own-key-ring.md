# Event.Passcode is encrypted in Api, with its own Data Protection key ring separate from Web's

ADR-0009 decided Passcode is encrypted-at-rest via the Data Protection API, but not where in the stack that
encryption happens or which app's key ring protects it. Data Protection was previously registered only in
`Web` (P2-2, #11), for auth cookies, anti-forgery tokens, and password-reset links — but `Event`'s DbContext,
and therefore any encryption tied to a column via an EF Core `ValueConverter`, lives in `Api`.

We decided `Event.Passcode` is encrypted and decrypted entirely in `Api`, at the EF Core boundary — plaintext
everywhere in the domain model and over the internal API, ciphertext only in the `Events` table's `Passcode`
column. `Api` gets its own Data Protection registration and its own key ring, persisted to the same Blob
Storage account `Web` already uses (P2-2's `dataprotection-keys` container) but under a separate key file
(`api-keys.xml` vs. Web's `keys.xml`) and a separate application name
(`VirtualLeadersGuide.Api` vs. `VirtualLeadersGuide`). Neither app can decrypt data the other one protected.

## Considered options

- **Encrypt in Web before the internal API call, store an opaque already-protected string in Api** — needs no
  new infrastructure in Api, but the internal API surface (P2-7, #16) would carry ciphertext over the wire, and
  "Passcode is always encrypted at rest" becomes a Web-side convention rather than something Api itself
  enforces — any other future caller of Api (a seed tool, a script, a bug) could write plaintext straight into
  the column.
- **Share one Data Protection key ring between Web and Api** — simpler (one key ring, one thing to back up),
  and would let a future feature decrypt Passcode from Web too without replumbing. Rejected: nothing today
  needs that, splitting a shared key ring's protected data apart later (if isolation ever turns out to matter)
  is much harder than merging two already-isolated ones would be, and a smaller blast radius if either app's
  key ring is ever compromised is worth the one extra blob file now.

## Consequences

`Api` needs a Blob Storage reference and its own `AddDataProtection()` call it never had before — a new
production dependency, and a new required secret (`ConnectionStrings__blobs`) on the `vlg-api` Container App
that didn't exist before this ticket (see `docs/runbooks/p2-2-blob-dataprotection-keys.md`). Like Web's key
ring (ADR stated there), `vlg-api` runs at `min-replicas 0` (ADR-0005), so this persistence is load-bearing,
not optional polish — an in-memory key ring would make every stored Passcode permanently undecryptable on the
next cold start.

Also worth noting, unrelated to this decision but adjacent to it: ADR-0009 states the production key ring
provider is Azure Key Vault, but the implementation actually landed on Blob Storage
(`docs/runbooks/p2-2-blob-dataprotection-keys.md`) — a pre-existing doc/implementation drift this ADR doesn't
attempt to resolve, since Api's key ring follows whatever Web already does either way.
