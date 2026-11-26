# Running tests

Run the tests before reporting work as finished, E2E included when the change touches it or a flow it drives.
If a suite can't run, say the work isn't done and why.

## Before running AppHost or E2E tests

- **Use PowerShell, not Git Bash.** In an elevated session Aspire's DCP can fail under Git Bash, and the only
  symptom is a two-minute boot timeout.
- **Docker Desktop is running.**
- **Nothing else is using this checkout's SQL volume.** `E2E.Tests` and `dotnet run` on the AppHost mount the
  same SQL Server data volume within one checkout, so only one of them can run at a time, and only one E2E run
  at a time. `AppHost.Tests` mounts no volumes. Other worktrees have their own volumes and don't collide.

If something else is running, it may be the user's. Ask before stopping any process or container.

## Commands

Run from the root of the checkout you're working in. A bare `dotnet test` (or Test Explorer's Run All) runs
everything. Run a single project to narrow things down:

```powershell
dotnet test
dotnet test tests/VirtualLeadersGuide.Api.Tests
dotnet test tests/VirtualLeadersGuide.Web.Tests
dotnet test tests/VirtualLeadersGuide.AppHost.Tests
dotnet test tests/VirtualLeadersGuide.E2E.Tests
```

Judge a run by its exit code. E2E can print `Passed!` and still fail its end-of-run cleanup check.

## When the stack doesn't come up

Every E2E test fails with `resource 'api' ... failed to start`. That message is the effect, not the cause. Get
the cause from the resource logs:

```powershell
dotnet test tests/VirtualLeadersGuide.E2E.Tests --logger "console;verbosity=normal"
```

| In the log | Cause |
| --- | --- |
| `sqlservr: Another instance of the application is already running` | Something else holds this checkout's SQL volume. |
| `The operation didn't complete within the allowed timeout` | Often DCP under Git Bash. |
| `Executable doesn't exist at ...chrome-headless-shell.exe` | Playwright browsers aren't installed. |
| `IDE returned unexpected status code`, only under Test Explorer | Aspire asked Visual Studio to launch `api` and `web`. Logged in `%TEMP%\aspire-dcp*\resource-executable-*.log`, not in the test output. |

To see which volume each container mounts:

```powershell
docker ps -q | ForEach-Object { docker inspect $_ --format "{{.Name}} {{range .Mounts}}{{.Name}} {{end}}" }
```

Playwright setup, traces and test data are in [`../runbooks/running-e2e-tests.md`](../runbooks/running-e2e-tests.md).
