# The E2E stack's lifetime is owned by an assembly-level xUnit test framework

`VirtualLeadersGuide.E2E.Tests` used to share one Aspire stack by putting every test class in a single xUnit
collection, `AspireE2ECollection : ICollectionFixture<AspireE2EFixture>` (ADR-0025). xUnit v2 runs the tests of
one collection serially, so the whole suite ran serially too - and a second collection fixture, the obvious way
to get parallelism, would boot a second stack on the same fixed launch-profile ports and persistent SQL volume.

P2.1-7 (#204) moves the stack's lifetime out of xUnit's fixture machinery and into the test run itself. An
assembly-level `[assembly: TestFramework]` (`AspireE2ETestFramework`) swaps in a custom
`XunitTestAssemblyRunner`, which boots the stack once in `AfterTestAssemblyStartingAsync` and, in
`BeforeTestAssemblyFinishedAsync`, sweeps orphaned data, verifies ADR-0039's retention table, and disposes the
stack once, after every collection has finished. `AspireE2EStack` holds the one `AspireE2EFixture`; test
classes read it through `E2ETestBase.Fixture` instead of having it injected, take no constructor arguments, and
declare no `[Collection]`. Each class is therefore its own implicit collection, so classes *can* run in
parallel (`xunit.runner.json`: `parallelizeTestCollections: true`) while tests inside a class stay serial.
`maxParallelThreads` is **1** by default, so the suite runs serially unless a run opts in (see below).

The decisions that are easy to get wrong:

- **The retention check runs inside the runner's `Aggregator`.** xUnit reports an exception held by the
  `Aggregator` at `BeforeTestAssemblyFinishedAsync` as a "Test Assembly Cleanup Failure", and `dotnet test`
  exits non-zero. A throwaway spike confirmed this under xUnit 2.9.3 / `xunit.runner.visualstudio` 3.1.5,
  with and without `--filter`. The test summary line can still read "Passed" - the cleanup failure is
  reported separately - so the exit code, not the summary, is the signal.
- **A boot failure is captured, not thrown.** `AfterTestAssemblyStartingAsync` is not run through the
  `Aggregator`, so a throw there would abort the run with one opaque error and no per-test results.
  `AspireE2EStack.BootAsync` stores the exception, appends it to `artifacts/e2e/BOOT_ERROR.txt`, and
  `AspireE2EStack.Current` rethrows it in every test - the same observable result a failing collection fixture
  gave. The sweep and retention check are skipped for a stack that never finished seeding, but its containers
  and processes are still disposed.
- **`AspireE2EFixture`'s constructor is `internal`.** xUnit can only build a class or collection fixture
  through a public constructor, so re-adding `ICollectionFixture<AspireE2EFixture>` or
  `IClassFixture<AspireE2EFixture>` fails at run start instead of quietly booting a second stack. This turns
  ADR-0025's "a reviewer has to catch it" into an enforced rule.
- **`EventManagementScenarios` runs alone, after the parallel phase**, through a fixture-less
  `NonParallelE2ECollection` (`DisableParallelization = true`): its Admin-grid row lookups assume the Event it
  just made is on page 1, which other classes creating Events concurrently can break. P2.1-8 (#205) removes
  that dependence and the collection with it.
- **Parallel is opt-in; the default cap is 1.** The ticket asked for four parallel classes to be measurably
  faster than serial. On the development machine (8 logical cores, 16 GB) they were not: with tracing off, serial
  took 1m52s and 1m45s and the `0.5x` cap (4 threads) took 2m12s and 1m49s, all 77 tests passing; and before
  tracing was turned off (ADR-0076), four threads were 2-4x *slower per test* and produced intermittent 30s
  timeouts and SQL connection timeouts. A serial run already held the CPU near 91%, and only about a third of
  the CPU in use was the test stack - Windows Defender, the developer's own browser and other background tools
  took the rest - so extra workers had no idle cores to use. The ticket's own figure, "one Chromium per worker",
  also assumes headroom a laptop doesn't have. The capability stays (a CI runner or workstation may well see a
  speedup) and is switched on per run with `dotnet test -- xUnit.MaxParallelThreads=N`; the default is the
  setting that was fastest and cleanest where it was measured. **The ticket's "4-thread run is measurably
  lower" criterion is therefore not met on this hardware** - the PR records the numbers rather than ship a
  default that is slower and flakier.
- **Appends to the shared artifact logs go through one lock** (`E2EArtifactLog`). Concurrent tests can write
  `CAPTURE_ERRORS.txt` / `TOO_LONG.txt` at the same moment, and an `IOException` there would escape
  `E2ETestBase.DisposeAsync`, which ADR-0028 forbids.

## Considered options

- **A static singleton booted lazily, disposed from `AppDomain.ProcessExit`** - rejected: nothing running in
  `ProcessExit` can fail the run, so ADR-0039's retention check would degrade from an enforced rule to a log
  line - the exact outcome ADR-0039 exists to prevent.
- **A second collection fixture per group of classes** - rejected: a second Aspire stack on the same fixed ports
  and SQL volume (ADR-0025).
- **xUnit v3 assembly fixtures** - rejected: blocked by the `xunit` hold-back in ADR-0060
  (`Microsoft.Playwright.Xunit` binds `xunit.extensibility.core` 2.8.0, and no v3 package exists).
- **Per-class Admin accounts** - not needed: a read-only audit found no scenario mutates the shared Admin,
  sign-in neither rotates the security stamp nor ends other sessions, and there is no failed-login lockout.
  If it is ever needed, the `admin-allowlist` parameter is a delimited list set at boot, so N fixed Admin
  emails need no production change - but they must be pre-declared, and the retention check and sweep updated.
- **A default cap of 4, or CPU-relative (`0.5x`)** - both tried, and rejected as the default for the reasons
  under "Parallel is opt-in" above: no speedup on the measured machine, and more flakiness. A CPU-relative cap
  also made a run's behaviour depend on which machine it ran on, which makes a flaky test harder to reproduce.

## Consequences

- The runner overrides `Aspire*` types from `xunit.execution`, which `xunit` brings in transitively. It is
  xUnit v2 API and will need rewriting if ADR-0060's hold-back on `xunit` is ever lifted.
- A new E2E test class needs no collection attribute and no constructor: derive from `E2ETestBase` and it runs
  in parallel with the others. A class that cannot run alongside others joins `NonParallelE2ECollection`.
- Per-test data must stay isolated from other concurrently running classes (ADR-0039's "always create your
  own"); the `emails/` copy in a failed test's artifacts can include other tests' emails, which is noisy but
  harmless.
- The same framework wraps every test case so a failed one is rerun once for evidence, which is also how a
  trace and video are produced without recording every test - see
  [ADR-0076](0076-a-failed-e2e-test-is-rerun-once-with-a-trace-and-video-for-evidence.md).
