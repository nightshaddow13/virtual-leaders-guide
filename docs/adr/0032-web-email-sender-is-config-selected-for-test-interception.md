# Web's `IEmailSender` is config-selected, so a test process can intercept it without a DI seam

The forgot-password → reset-link → sign-in-with-new-password flow (P2.1-4, #62) is untested end to end: the
reset link only ever leaves the app inside an email, and `AcsEmailSender` (P2-1, #1) `new`s an `EmailClient`
inline per send, with no injected dependency to substitute. Under Aspire, `Web` also runs as a separate OS
process from the E2E test process, so `Web.Tests`' `WebApplicationFactory<Program>` trick — swapping a fake
into DI before the host builds — doesn't apply either; there is no DI container the test process can reach.
Configuration crossing the process boundary via environment variables (the same mechanism `AppHost.cs` already
uses for every other resource-to-resource setting) is the only lever available.

We add a second `IEmailSender<ApplicationUser>` implementation, `FileSinkEmailSender`, which writes each email
to a JSON file instead of sending it, and select between it and `AcsEmailSender` at startup via
`Email:Provider` (`Acs`, the default, or `FileSink`). The E2E fixture sets `Email__Provider=FileSink` and
`Email__FileSinkDirectory` as environment variables on the `web` resource between `CreateAsync` and
`BuildAsync`, and polls that directory for the email a test just triggered.

Selecting the file sink also requires `Email:FileSinkAllowed=true`, which only `AppHost.cs` ever sets, inside
its existing `!builder.ExecutionContext.IsPublishMode` block (ADR-0013 uses the same guard for
`Migrations:ApplyAutomatically`). That block is absent from `aspire publish`'s deploy manifest by
construction, so the flag cannot reach a deployed environment even by accident — a real deployment can select
the file sink only if someone hand-edits its config directly, and startup still refuses it there. This is
deliberately not an `ASPNETCORE_ENVIRONMENT`/`IsProduction()` check: ADR-0013 already found that
`Aspire.Hosting.Testing`'s `DistributedApplicationTestingBuilder` doesn't reliably propagate launch-profile
environment variables to child project resources, making that gate flaky under exactly the test harness this
feature exists to serve. An unrecognized `Email:Provider` value also throws at startup rather than silently
falling back to `AcsEmailSender` — a typo in the fixture's config would otherwise surface as a test hung
waiting on an email that was never going to arrive, instead of a clear startup failure.

The written shape, `SentEmailDto` (`To`, `Subject`, `Kind`, `Payload`, `SentAtUtc`), lives in
`VirtualLeadersGuide.Identity.Contracts` rather than being duplicated across `Web` and `E2E.Tests`: both
projects already reference that assembly, so a field rename becomes a compile error instead of a test-timeout
mystery. `FileSinkEmailSender` implements all three `IEmailSender<TUser>` methods, not just
`SendPasswordResetLinkAsync` — see Consequences for what that costs.

## Considered options

- **A stub HTTP listener speaking ACS's `emails:send` API**, fed to the existing `acs-connection-string`
  parameter — zero shipping-code change, and it would exercise `AcsEmailSender` itself rather than a parallel
  implementation only tests take. Rejected without a deeper spike: `EmailClient`'s long-running-operation
  polling, TLS expectations, and `api-version` handling are all unverified against a stub, and it would couple
  the test suite to ACS's wire protocol - a client library upgrade could break tests for reasons unrelated to
  this app.
- **Gate the file sink on `ASPNETCORE_ENVIRONMENT`/`IsProduction()`**, as originally scoped — rejected once
  cross-checked against ADR-0013's finding that the E2E fixture's `DistributedApplicationTestingBuilder`
  doesn't reliably propagate that variable to `web` in the first place, which would make the guard both unable
  to prove itself safe under E2E and prone to firing when it shouldn't.
- **Assert on logs instead of a file** — rejected because it would mean logging a live, single-use password
  reset link in production, which is a worse leak surface than a fail-closed file sink.
- **Call `UserManager.GeneratePasswordResetTokenAsync` directly from the test**, skipping the email step
  entirely — rejected because it skips `ForgotPassword.razor`, which is the actual surface under test; a test
  written this way would not catch a regression in the page itself.

## Consequences

- `AcsEmailSender` and `FileSinkEmailSender` are not behaviourally equivalent: the file sink writes a file for
  `SendConfirmationLinkAsync`/`SendPasswordResetCodeAsync`, where `AcsEmailSender` throws
  `NotSupportedException` (nothing in this app calls either today). A future stray call to one of those two
  would pass under the E2E suite and fail in production.
- The E2E suite never exercises `AcsEmailSender` itself - only the file sink. An ACS-side regression (wrong
  sender address, a broken `EmailContent` shape) stays invisible to this suite; only manual verification or a
  production incident would surface it.
- `SentEmailDto` - a test-interception format, not a wire contract between `Api` and `Web` - lives in an
  assembly named for identity, alongside genuine Api↔Web DTOs. Accepted for the compile-time safety it buys
  over duplication (see above); a second unrelated cross-cutting test format landing there would be a signal
  to reconsider.
- Shipping code (`Program.cs`, `AppHost.cs`) permanently carries a branch whose only purpose is serving the
  test suite, gated fail-closed by `Email:FileSinkAllowed`. This is the cost accepted for real-browser coverage
  of the reset flow; see Considered Options for why the alternatives that avoid it weren't taken.
