---
status: narrows ADR-0012's test-naming rule for E2E.Tests
---

# `E2E.Tests` names scenarios `GivenX_WhenY_ThenZ` in `...Scenarios` classes, narrowing ADR-0012

ADR-0012 names test classes `{ClassUnderTest}Should` and test methods as a `Verb_When_For` sentence. That
convention already sat awkwardly on this project before P2.1-3 (#61): `DashboardAuthorizationShould` isn't
named after a class under test at all - there is no `DashboardAuthorization` class - it's named after a
concern (`/dashboard`'s authorization gate across three states). `E2E.Tests` tests user-facing scenarios
driven through a real browser, not one class's behavior, which `{ClassUnderTest}Should` assumes.

Methods in this project are instead named `GivenX_WhenY_ThenZ`, and classes take a `...Scenarios` suffix in
place of `...Should` (`LoginPageScenarios`, `DashboardAuthorizationScenarios`, `NavMenuScenarios`). Given/When/
Then was picked specifically because this repo already writes issue Acceptance Criteria in that exact grammar
(P2.1-3/#61's own ACs included) - a test method named `GivenAnAnonymousUser_WhenNavigatingToDashboard_
ThenItRedirectsToLoginWithAReturnUrl` reads as the literal AC it proves, rather than requiring a reader to
mentally translate between two different phrasings. `Scenarios` was picked over alternatives because it
matches the methods' own new grammar directly: a class full of `GivenX_WhenY_ThenZ` methods is, literally, a
list of scenarios.

Every `[Fact]` also carries a `DisplayName` matching its identifier in plain English (e.g. `[Fact(DisplayName
= "Given an anonymous user, when navigating to /dashboard, then the browser redirects to Account/Login with a
returnUrl")]`). The two forms say the same thing on purpose, rather than one being a terser identifier and the
other free prose - there's no second string to let drift out of sync with the first. This was originally
intended to feed Allure's dashboard rendering (P2.1-3's AC #3), which was later split out to #71 after
Allure's reporter mechanism proved not to activate under `dotnet test` in this environment - but the
`DisplayName` stands on its own regardless: it's what the default VSTest console logger renders in place of
the raw identifier, confirmed directly during implementation, and it's what any future reporting layer would
read from without needing every test method touched again.

## Considered options

- **`DisplayName`-only, with a minimal/short C# identifier** (e.g. a numbered or abbreviated method name,
  with the full scenario living only in `DisplayName`) - rejected because it reintroduces exactly the
  drift risk the paired form avoids: the identifier and the human-readable description could diverge, and a
  stack trace or `<Class>.<Method>` artifact folder name (P2.1-3's own AC #1) would show the terse form, not
  the readable one.
- **Rename classes to a `...Feature` suffix instead of `...Scenarios`**, closer to Gherkin's own vocabulary
  (`.feature` files group `Scenario`s) - rejected because it implies a 1:1 mapping onto a product feature that
  doesn't hold for every class here: `NavMenuScenarios` tests a UI component's behavior, not a "NavMenu
  feature," and `LoginPageScenarios` is scoped to a page, not a feature boundary.
- **Leave `...Should`/`Verb_When_For` unchanged and accept the awkward fit** - rejected; the friction was
  concrete enough (`DashboardAuthorizationShould` naming a concern, not a class) that it was worth a
  documented, narrow exception rather than living with a convention that already didn't describe what this
  project's classes are.

## Consequences

- `E2E.Tests` now has two ways test methods are named across this repo: `Verb_When_For` everywhere else
  (`Api.Tests`, `AppHost.Tests`, `Web.Tests`, all under ADR-0012 unchanged), and `GivenX_WhenY_ThenZ` here
  only. A reviewer used to one should expect the other specifically in this project, not treat it as drift.
- Any future E2E test class added to this project follows `...Scenarios`/`GivenX_WhenY_ThenZ` with a matching
  `DisplayName`, not ADR-0012's convention - this ADR is the record of why, the same way ADR-0025 is for this
  project's location in `tests/`.
- The `<Class>.<Method>` segment of P2.1-3's artifact folder path (`artifacts/e2e/<timestamp>/<Class>.
  <Method>/`) is now built from these longer Given/When/Then names rather than the shorter `Verb_When_For`
  ones - directly motivating the path-length defense documented in ADR-0028.
