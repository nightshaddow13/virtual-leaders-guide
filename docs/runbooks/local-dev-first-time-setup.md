# Local development: first-time setup

Gets the app running locally for the first time and creates your first account to sign in with. Assumes the
repo is already cloned, the .NET 10 SDK is installed, and Docker Desktop is running (AppHost manages a real
SQL Server container and an Azurite container locally, per ADR-0001/ADR-0014).

## 1. Required user-secrets

`AppHost.cs` declares three secret parameters with no default value - fail-closed, same philosophy as
`internal-api-key` (ADR-0015). The AppHost will not start without them.

```powershell
# Shared secret between Web and Api - any string works locally, it just has to match on both sides, which
# AppHost.cs already guarantees by injecting the same parameter into both.
dotnet user-secrets set "Parameters:internal-api-key" "local-dev-key" --project src/VirtualLeadersGuide.AppHost

# Signs/validates the internal JWT Web mints to forward a signed-in user's identity to Api (P2-5, #14,
# ADR-0007) - a separate secret from internal-api-key above, since the two answer different trust questions.
# Any string works locally; it just has to match on both sides, same as internal-api-key.
dotnet user-secrets set "Parameters:internal-jwt-key" "local-dev-jwt-key" --project src/VirtualLeadersGuide.AppHost
```

For `acs-connection-string` (Azure Communication Services Email, [P2-1](p2-1-acs-email-provisioning.md)):

- If you have a real ACS resource provisioned, use its real connection string - that's what lets
  `Account/ForgotPassword` actually send an email.
- Otherwise, any placeholder string satisfies the fail-closed startup check. Password reset will fail when
  you actually submit it, but everything else (sign-in, sign-out, the dashboard gate) works fine without it.

```powershell
dotnet user-secrets set "Parameters:acs-connection-string" "<real-or-placeholder-value>" --project src/VirtualLeadersGuide.AppHost
```

Also set your own email as the Admin allowlist (P2-4, #13; ADR-0008) - unlike the three secrets above, this
one isn't fail-closed (an unset/empty value just means "no Admins yet"), but skipping it means your seeded
account in step 3 lands on `/Account/NoAccess` instead of the dashboard:

```powershell
dotnet user-secrets set "Parameters:admin-allowlist" "you@example.com" --project src/VirtualLeadersGuide.AppHost
```

No `sqlserver-password` step needed - Aspire generates one automatically the first time `AddAzureSqlServer`
runs.

## 2. Run the app

```powershell
dotnet run --project src/VirtualLeadersGuide.AppHost
```

First run pulls the `mssql/server` and `azurite` container images, which can take a minute or two;
subsequent runs are fast. The Aspire dashboard (URL printed to the console) shows each resource's status and
assigned ports - `api` and `web` default to the ports in their own `launchSettings.json` (`5058`/`7223` and
`5257`/`7186` respectively) unless Aspire assigns different ones for your session.

## 3. Create your first account

There is no Register page. `tools/VirtualLeadersGuide.Tools.SeedUser` is a small checked-in dev tool (kept
structurally separate from `src/` - see
[ADR-0023](../adr/0023-developer-tooling-lives-in-top-level-tools-folder.md)) that POSTs one account directly
to Api's internal identity endpoint with a real ASP.NET Core Identity password hash, so it can actually sign
in afterward.

Run it while the AppHost is up, using the `internal-api-key` value from step 1:

```powershell
dotnet run --project tools/VirtualLeadersGuide.Tools.SeedUser -- --api-key local-dev-key --email xgoss@live.com
```

`--email` prompts if omitted; `--api-url` defaults to `http://localhost:5058` (Api's local launch profile
port - override it if the Aspire dashboard assigned a different one for your session). The password is
always prompted for interactively, masked - it's never a command-line argument, so it never ends up in shell
history. Run `--help` for the full option list.

Sign in at `/Account/Login` (e.g. `https://localhost:7186/Account/Login`) with that email and the password
you typed. If that email matches the Admin allowlist you set in step 1, you're promoted to Admin on this very
sign-in (P2-4, #13; ADR-0008) and land on `/dashboard`. If you skipped that step, or seeded a different email,
you'll land on `/Account/NoAccess` instead - set/update the allowlist to your seeded email and sign in again.

## Verification

- Sign-in succeeds and the NavMenu shows your email.
- With your seeded email on the Admin allowlist, `/dashboard` renders its placeholder page. Otherwise it
  redirects to `/Account/NoAccess` with a working sign-out link.
- Signing out returns the NavMenu to "Sign in".
- Restarting just the `web` resource from the Aspire dashboard keeps you signed in - if it doesn't, the
  Data Protection key ring isn't persisting correctly (see [P2-2's Blob Storage
  runbook](p2-2-blob-dataprotection-keys.md)).
