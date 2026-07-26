# Test classes are named `{ClassUnderTest}Should`, test methods read as a sentence

Test classes are named `{ClassUnderTest}Should` (e.g. `AppHostShould`), and `[Fact]`/`[Theory]` methods follow
`{ExpectedOutcome}_When{Condition}_For{MethodUnderTest}` (e.g.
`BuildAndStartSuccessfully_WhenNoResourcesAreRegistered_ForStartAsync`), so a failing test's fully qualified name
reads as a sentence: "AppHost Should Build And Start Successfully When No Resources Are Registered For Start
Async." This is the first test class in the repo (P1-2), so — like ADR-0010/0011 — the convention had no existing
precedent to follow. We picked this over the common `MethodName_Scenario_ExpectedResult` pattern because a test
failure surfaces as an assertion about *behavior* ("Should build and start successfully") rather than an
implementation detail ("DistributedApplication_BuildsAndStartsSuccessfully"), which reads better in CI output and
test explorers where only the method name is visible at a glance. Once many test classes exist under this
pattern, renaming every class/method to a different convention means touching every test file in the repo —
cheap now, increasingly disruptive as more test projects get added on top.

## Considered options

- `MethodName_Scenario_ExpectedResult` (e.g. `StartAsync_NoResourcesRegistered_StartsSuccessfully`) — a very
  common xUnit convention, but reads as three loosely-joined nouns rather than a sentence, and puts the
  method-under-test first rather than the behavior being asserted.
- Bare `[Fact]` names with no enforced structure, relying on the method body/`Assert` calls to convey intent —
  fewer rules to follow, but gives up the "reads as a sentence" property entirely and leaves naming inconsistent
  across contributors/agents.
