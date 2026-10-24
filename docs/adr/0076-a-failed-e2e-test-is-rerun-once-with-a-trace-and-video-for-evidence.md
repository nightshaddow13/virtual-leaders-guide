# A failed E2E test is rerun once, with a trace and video, purely for evidence

Until P2.1-7 (#204) every E2E test recorded a Playwright trace (screenshots, DOM snapshots, sources) and a video
for its whole run, then threw both away unless the test failed (ADR-0028). That is cheap to write and expensive
to run: measured on this repo's 77-test suite on a developer laptop, a serial run took 3m38s-4m19s of test time
with tracing and **2m45s** without - about 36% less. The tracing was also the dominant source of disk churn:
Windows Defender spent roughly **300 CPU-seconds** scanning the trace and video files in one run, against about
27 once tracing was off, and the Playwright driver's CPU fell from 81s to 25s. Almost all of that cost is paid by
tests that pass, which is nearly all of them, for evidence nobody opens.

So a test's **first attempt records neither a trace nor a video**. When it fails, it is **rerun once with both
on**, and the rerun's artifacts are what land in the failure folder.

- **The reported result is always the first attempt's.** `RerunOnFailureTestCase` wraps every xUnit test case
  (`AspireE2ETestFrameworkExecutor` does the wrapping, so test classes carry no attribute), holds back the first
  attempt's messages until the rerun finishes, forwards them unchanged, and discards the rerun's own messages.
  A rerun that passes therefore never turns a failure into a pass: it writes `rerun/DID_NOT_REPRODUCE.txt`
  instead of a trace, and the test is still red. Nothing the rerun throws can change the reported result.
- **The first attempt still captures what needs no recording** - `screenshot.png`, `page.html` and the
  `emails/` copy, taken at the moment of failure. The rerun's artifacts go in a `rerun/` subfolder of the same
  test folder, so neither overwrites the other.
- **`VLG_E2E_TRACE_ALL=1` records every attempt and disables the rerun**, for a failure that is too intermittent
  to reproduce on demand (a trace of the one failing attempt is then the only useful evidence), or for anyone
  who wants the old always-on behaviour while debugging locally.
- **A test passing on its first attempt leaves nothing behind**, as before (AC #2 of #61) - and now also writes
  no trace or video to disk in the first place.

## Considered options

- **Keep recording every test (the status quo)** - rejected: it is the single biggest cost in the suite, and
  nearly all of it is wasted.
- **Playwright's `retain-on-failure`-style behaviour** - not a saving here: the cost is in *recording*, and
  `retain-on-failure` still records every test and merely deletes the output afterwards, which is exactly what
  this suite did already.
- **Opt-in recording only (`VLG_E2E_TRACE_ALL=1` by default off, no rerun)** - rejected as the only mechanism:
  a developer would have to notice a CI or local failure and then reproduce it with the flag set, which is the
  step this suite exists to avoid (ADR-0028's whole premise is that the evidence is there when the test fails).
  It stays as the escape hatch above.
- **Retry the test and report the retry's result (a "flaky retry")** - rejected outright: it would hide real
  intermittent failures, which are exactly what this suite should surface. The rerun exists to gather evidence,
  never to decide pass/fail.

## Consequences

- A failing test takes roughly twice as long, and a non-reproducing failure leaves no trace or video. Both are
  paid only on failure.
- ADR-0028 rejected a retry mechanism as premature because nothing needed one; this is the concrete driver it
  was waiting for, and it is recorded there as an amendment.
- The rerun creates and deletes its own throwaway data like any other attempt (ADR-0039), so it needs no extra
  cleanup - but a test that is not safe to run twice against one database (it relies on a fixed, non-guid name)
  would fail on the rerun for the wrong reason. Every scenario here guid-suffixes its data; a new one must too.
- `E2ECaptureMode` carries the "this is the rerun" flag in an `AsyncLocal`, not a static, because classes run
  in parallel (ADR-0075) and only the rerun's own async flow may see it.
