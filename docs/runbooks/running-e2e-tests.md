# Running the E2E test suite

Runs `tests/VirtualLeadersGuide.E2E.Tests` against the real Aspire-orchestrated stack - Api and Web as
separate processes, a real SQL Server container, real Azurite - driven by a real (headless) browser via
Playwright. Distinct from `dotnet test` on the rest of the solution, which none of this touches.

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

## 2. Install Playwright's browsers (first time only)

There's no `pwsh` (PowerShell 7) dependency here, even though Playwright's own generated
`playwright.ps1` script assumes one - that script loads a `net10.0` assembly, which Windows PowerShell
5.1 (.NET Framework) can't do. Use the package's own bundled Node driver instead, which needs nothing
beyond what the `Microsoft.Playwright.Xunit` package already restored:

```powershell
cd tests/VirtualLeadersGuide.E2E.Tests/bin/Debug/net10.0
.\.playwright\node\win32_x64\node.exe .playwright\package\cli.js install chromium
```

This downloads Chromium, its headless-shell variant, and ffmpeg (needed for video capture - see
Section 4) into `%LOCALAPPDATA%\ms-playwright`. Re-run it if a `dotnet test` run starts failing every
test with `Executable doesn't exist at ...chrome-headless-shell.exe` - the cache can end up stale or
partially populated (e.g. after a package upgrade) without any other symptom.

## 3. Run the suite

```powershell
dotnet test tests/VirtualLeadersGuide.E2E.Tests
```

Expect this to take a couple of minutes - `AspireE2EFixture` boots the whole stack once for the entire
run (container pulls on a cold cache add more). Every test shares that one boot via
`AspireE2ECollection`, so they run sequentially, not in parallel.

## 4. Where failure artifacts land

Every failed test leaves `trace.zip`, `screenshot.png`, `video.webm`, and `page.html` under:

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

## 5. Viewing a trace

`trace.zip` is a Playwright trace: a time-travel view of every action, a screenshot filmstrip, the
network log, and the console log for that one test run.

Drag it onto **[trace.playwright.dev](https://trace.playwright.dev)** - runs entirely in the browser,
nothing uploaded. This is the primary path on this machine, since `pwsh bin/Debug/net10.0/playwright.ps1
show-trace` (the usual local alternative) isn't runnable without PowerShell 7 installed.

## 6. Allure reporting

Not currently wired up - `Allure.Xunit`'s reporter doesn't activate under `dotnet test` in this
environment (tracked as [#71](https://github.com/nightshaddow13/virtual-leaders-guide/issues/71), with
the full diagnostic trail). `trace.zip` plus `screenshot.png`/`video.webm`/`page.html` are the whole
diagnostic story for now.
