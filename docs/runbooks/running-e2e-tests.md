# Running the E2E test suite

Runs `tests/VirtualLeadersGuide.E2E.Tests` against the real Aspire-orchestrated stack - Api and Web as
separate processes, a real SQL Server container, real Azurite - driven by a real (headless) browser via
Playwright. Distinct from `dotnet test` on the rest of the solution, which none of this touches.

The suite also forces `Email__Provider=FileSink` onto `web`, so no password-reset email actually reaches
Azure Communication Services during a run - see
[ADR-0032](../adr/0032-web-email-sender-is-config-selected-for-test-interception.md). Emails land as JSON
files in a run-scoped temp directory (`AspireE2EFixture.EmailSink`), not under `artifacts/`, and are copied
into a failed test's own artifact folder (Section 6) rather than left in temp.

## 1. Prerequisites

Same three things `AspireE2EFixture` itself checks for and reports on failure:

- Docker Desktop is running (the suite boots a real SQL Server container and Azurite, same as
  [`VirtualLeadersGuide.AppHost.Tests`](../adr/0014-dac-tests-use-sqlite-not-real-sql-container.md)).
- `dotnet dev-certs https --trust` has been run at least once on this machine - `Web` unconditionally
  redirects HTTP to HTTPS, so every navigation fails on a cert error without it.
- No other AppHost is running. This suite cannot run alongside `dotnet run --project
  src/VirtualLeadersGuide.AppHost` or `VirtualLeadersGuide.AppHost.Tests` - all three share the same
  fixed launch-profile ports and the same persistent SQL Server data volume.

Playwright's browsers also need to be installed once per machine (see below) - unlike the three checks
above, nothing in the suite itself detects a missing browser install cleanly; it just fails every test
with `Executable doesn't exist at ...`.

## 2. Optional: install PowerShell 7 (`pwsh`)

Nothing else in this runbook requires it - Section 3's browser install and Section 7's trace viewing
both work without it, via workarounds that don't. Install it anyway if you'd rather use Playwright's own
generated `playwright.ps1` script (`install`, `show-trace`, `codegen`, ...) instead:

```powershell
winget install --id Microsoft.PowerShell --source winget
```

Restart your terminal afterward so `pwsh` is picked up on `PATH` (`pwsh -Version` to confirm). Once
installed, Section 3's browser install and Section 7's trace viewing can both use `playwright.ps1`
directly instead of their workarounds - see each section.

## 3. Install Playwright's browsers (first time only)

Every command in this runbook, including these, is written to run from the **repo root** - no `cd`
needed anywhere.

Without `pwsh`, Playwright's own generated `playwright.ps1` script doesn't run - it loads a `net10.0`
assembly, which Windows PowerShell 5.1 (.NET Framework) can't do. Use the package's own bundled Node
driver instead, which needs nothing beyond what the `Microsoft.Playwright.Xunit` package already
restored:

```powershell
tests/VirtualLeadersGuide.E2E.Tests/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe tests/VirtualLeadersGuide.E2E.Tests/bin/Debug/net10.0/.playwright/package/cli.js install chromium
```

With `pwsh` installed (Section 2), the standard command works instead:

```powershell
pwsh tests/VirtualLeadersGuide.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Either way, this downloads Chromium, its headless-shell variant, and ffmpeg (needed for video capture -
see Section 6) into `%LOCALAPPDATA%\ms-playwright`. Re-run it if a `dotnet test` run starts failing every
test with `Executable doesn't exist at ...chrome-headless-shell.exe` - the cache can end up stale or
partially populated (e.g. after a package upgrade) without any other symptom.

If you'd rather work from `bin/Debug/net10.0` directly (e.g. `cd tests/VirtualLeadersGuide.E2E.Tests/bin/Debug/net10.0`
first), every path below and in Section 7 needs the same prefix stripped off - `playwright.ps1` and
`.playwright\...` instead of the full `tests/...` path, and `artifacts/e2e` (Section 7's search root)
becomes `../../../../../artifacts/e2e` (five levels back up to the repo root).

## 4. Run the suite

```powershell
dotnet test tests/VirtualLeadersGuide.E2E.Tests
```

Expect this to take a couple of minutes - `AspireE2EFixture` boots the whole stack once for the entire
run (container pulls on a cold cache add more). Every test shares that one boot via
`AspireE2ECollection`, so they run sequentially, not in parallel.

## 5. Test data

The suite maintains a fixed set of fixture data across every run - four accounts
(`e2e-admin@example.test`, `e2e-director@example.test`, `e2e-norole@example.test`,
`e2e-invited@example.test`, all sharing the password in `TestCredentials.KnownPassword`) and one retained
Event (`e2e-retained-event`) - and deletes everything else it creates, including a crashed or killed run's
leftovers, via a run-end sweep in `AspireE2EFixture.DisposeAsync`. See
[ADR-0039](../adr/0039-e2e-tests-clean-up-after-themselves.md) for the full retention table and why. Anything
outside the `@example.test` domain (a real account you made by hand) is never touched.

Set `VLG_E2E_KEEP_DATA=1` before running to skip per-test cleanup and the run-end sweep, for inspecting a
real post-run database instead of only a `trace.zip`:

```powershell
$env:VLG_E2E_KEEP_DATA = "1"
dotnet test tests/VirtualLeadersGuide.E2E.Tests
```

If the fixture data ever ends up corrupted (e.g. a manual edit, or a run killed before the fixture itself
finished seeding), delete the four accounts and the retained Event by hand - the next run re-seeds them
idempotently.

## 6. Where failure artifacts land

Every failed test leaves `trace.zip`, `screenshot.png`, `video.webm`, `page.html`, and an `emails/` folder
(any JSON files the file-sink email sender had written by the time the test failed) under:

```
artifacts/e2e/<run timestamp>/<Class>.<Method>/
```

A passing run leaves no `artifacts/e2e/` folder at all - nothing to clean up. `artifacts/` is already
gitignored.

Two fallback markers can appear at the `artifacts/e2e/<run timestamp>/` level instead of a per-test
folder, if something about capturing a specific file didn't go to plan:

- **`TOO_LONG.txt`** - one or more resolved artifact paths would have exceeded a safe length under
  Windows' `MAX_PATH` (260 characters; this is a real constraint unless [Windows long-path support]
  is enabled). Lists which file, for which test, and its would-be full path.
- **`CAPTURE_ERRORS.txt`** - anything else that went wrong capturing a specific file (a transient I/O
  error, a locked file handle). The test's own reported pass/fail result is never affected either way -
  a capture problem only ever costs evidence, never correctness of the result.

[Windows long-path support]: https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation

## 7. Viewing a trace

`trace.zip` is a Playwright trace: a time-travel view of every action, a screenshot filmstrip, the
network log, and the console log for that one test run.

Without `pwsh`, drag it onto **[trace.playwright.dev](https://trace.playwright.dev)** instead - runs
entirely in the browser, nothing uploaded. With `pwsh` installed (Section 2), the local viewer works
too. Rather than typing out a `<run timestamp>/<Class>.<Method>` path by hand, find the most recent
trace automatically:

```powershell
$trace = Get-ChildItem artifacts/e2e -Filter trace.zip -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
pwsh tests/VirtualLeadersGuide.E2E.Tests/bin/Debug/net10.0/playwright.ps1 show-trace $trace.FullName
```

## 8. Allure reporting

Not currently wired up - `Allure.Xunit`'s reporter doesn't activate under `dotnet test` in this
environment (tracked as [#71](https://github.com/nightshaddow13/virtual-leaders-guide/issues/71), with
the full diagnostic trail). `trace.zip` plus `screenshot.png`/`video.webm`/`page.html` are the whole
diagnostic story for now.
