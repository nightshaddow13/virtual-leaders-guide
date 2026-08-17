# E2E pass/fail is decided by an explicit `RunAsync` wrapper, not `Microsoft.Playwright.Xunit`'s `ExceptionCapturer`

`Microsoft.Playwright.Xunit`'s test base classes (`PageTest` → `ContextTest` → `BrowserTest` → `PlaywrightTest`
→ `WorkerAwareTest` → `ExceptionCapturer`) decide pass/fail via `AppDomain.FirstChanceException`: any exception
thrown anywhere in the process while a test is running - caught and handled or not - marks that test failed.
P2.1-3 (#61) needs a much narrower signal than that to decide when to capture `trace.zip`/`screenshot.png`/
`video.webm`/`page.html`: specifically, whether *this test's own body* threw. `FirstChanceException` fires for
exceptions the code already recovers from (a probe's expected timeout inside `AspireE2EFixture`, a caught
exception inside Aspire's own resource orchestration), which would otherwise mark a genuinely passing test as
failed and capture artifacts for a test that never actually failed.

Every `[Fact]`/`[Theory]` body in this project is instead wrapped in `E2ETestBase.RunAsync(Func<Task>
testBody)`. `_passed` defaults to `false` and only flips to `true` once `testBody` returns without throwing;
`RunAsync` adds no `try`/`catch` of its own, so an assertion failure still propagates to xUnit exactly as it
would unwrapped. `_passed` defaulting to `false` is deliberate: a test that forgets the `RunAsync` wrapper
over-collects artifacts (every failure-path capture in `DisposeAsync` runs) rather than silently losing the
evidence it exists to preserve.

`RunAsync` also takes an optional `[CallerMemberName]` `testName` parameter, overridable by the caller. No
`[Theory]` exists in this project yet, but the mechanism is deliberately in place now: `[CallerMemberName]`
alone can't distinguish one data row from another, and this project pins xUnit v2, where `TestContext.Current`
(which would otherwise recover the running test's identity from inside the framework) doesn't exist -
reaching into xUnit's own internals to recover it was rejected in favor of the caller just supplying its own
row-inclusive name. Retry/flaky-re-run disambiguation was considered for the same mechanism and rejected as
premature - no test project in this repo has a retry mechanism today, so there's nothing concrete to design
against yet.

A related principle, discovered empirically rather than designed up front: **no artifact-capture failure may
ever propagate out of `DisposeAsync`.** Two independent failure modes were hit during implementation, and both
are handled the same way - swallowed and logged, never rethrown:

- A resolved artifact path exceeding a conservative length budget (this machine has `LongPathsEnabled=0`,
  and Given/When/Then method names run long) is caught by `ResolveArtifactPath` before any write is attempted,
  and logged to a per-run `TOO_LONG.txt`.
- Anything else - observed directly as `File.Move`ing the finalized video into place throwing
  `IOException: The process cannot access the file because it is being used by another process`, because
  Playwright's video encoder can still hold the temp file's handle for a short window after
  `base.DisposeAsync()` returns and `IVideo.PathAsync()` resolves - is caught by the general
  `TryCaptureAsync` backstop and logged to `CAPTURE_ERRORS.txt`. The video-move race specifically is retried
  with a short backoff first, since the handle reliably releases within a couple of seconds in practice, but
  `TryCaptureAsync` is what makes that retry safe to add without also having to reason about every other way
  a capture step could throw.

Without this, an unguarded capture failure doesn't just lose one file - as observed directly on the very first
test run of the failure path, it corrupts the reported result: xUnit reported the test as failing with a
`System.AggregateException` wrapping *both* the real `PlaywrightException` assertion failure *and* the
unrelated video `IOException`, turning a single clear failure message into two. A path-length problem or a
locked file handle may only ever cost captured evidence - never the correctness of the reported result.

## Considered options

- **Rely on `Microsoft.Playwright.Xunit`'s built-in pass/fail signal** - rejected outright per the issue's own
  Dev Notes: `FirstChanceException`-based capture marks passing tests as failed on any caught-and-handled
  exception anywhere in the process, which this fixture's own retry/polling logic triggers routinely.
- **Recover the running test's identity from xUnit's own internals for `[Theory]` disambiguation** (reflecting
  into `ITestOutputHelper`'s backing `ITest`, or similar) - rejected in favor of an explicit parameter: it
  would work today but ties this project to xUnit v2's specific internal shape, is exactly the kind of thing
  `TestContext.Current` exists to replace in v3, and a caller passing its own name is simpler and more
  portable either way.
- **Let an artifact-capture failure propagate and fail the test a second, different way** - rejected once the
  `AggregateException` corruption was observed directly: it defeats the entire point of this ticket, which is
  to make a failure easier to diagnose, not harder.
- **Hard-fail (throw) on a too-long path or a locked file, rather than log and continue** - rejected for the
  same reason as above; considered and rejected explicitly during the design of `ResolveArtifactPath`.

## Consequences

- Any future capture step added to `DisposeAsync` must go through `TryCaptureAsync` (or an equivalent
  swallow-and-log wrapper) rather than writing a file directly - a bare `File.*`/`Page.*` call in the failure
  branch is exactly the mistake this ADR documents having made and then fixed.
- `TOO_LONG.txt` and `CAPTURE_ERRORS.txt` are two different files by design, not merged into one generic
  "capture-issues" log: `TOO_LONG.txt` is a known, anticipated condition with a specific cause and fix (a
  shorter name, or enabling Windows long-path support); `CAPTURE_ERRORS.txt` is a catch-all for anything
  unanticipated. Conflating them would make the common, expected case harder to scan for.
