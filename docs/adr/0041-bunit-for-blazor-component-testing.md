# bUnit (plus NSubstitute) for Blazor component testing

ADR-0040's code-behind migration moved 13 components' `@code` into `.razor.cs` partial classes specifically
so that logic became unit-testable - but testing it means rendering a Blazor component and driving its
lifecycle methods, which this repo had no way to do. Every existing test either drives a plain class directly
(`ApiEventClientShould`, `InternalJwtProviderShould`, ...) or goes through `WebApplicationFactory` over real
HTTP (`DashboardShould`, `SignInShould`, ...) - neither reaches a component's own `OnInitializedAsync`/
`OnParametersSetAsync`/event-handler logic, or lets a test assert on what actually rendered.

## The rule

**bUnit** (`bunit`, v2.9.0) is this repo's component-testing library, added to
`VirtualLeadersGuide.Web.Tests`. A test class extends `Bunit.BunitContext` (not the `Bunit.TestContext` alias
- obsolete in this version, slated for removal), registers whatever fakes the component under test needs in
`Services`, and renders with `Render<TComponent>(...)`.

**NSubstitute** (`NSubstitute`, v6.2.0) is added alongside it, specifically to fake
`UserManager<TUser>`/`SignInManager<TUser>` - both are concrete classes with no interface, but ASP.NET Core
Identity makes their members `virtual` for exactly this purpose. `FakeUserManagerFactory`
(`tests/VirtualLeadersGuide.Web.Tests/FakeUserManagerFactory.cs`) builds a substitute with every constructor
argument it doesn't need either substituted or left `null` - `UserManager<TUser>`'s own constructor defaults
the ones that need one, and no test lets a call fall through to the real `IUserStore<TUser>` behind it;
every test configures the manager's own method directly via `.Returns(...)` instead.

`InternalsVisibleTo("VirtualLeadersGuide.Web.Tests")` was added to `VirtualLeadersGuide.Web`
(`src/VirtualLeadersGuide.Web/AssemblyInfo.cs`) alongside this - the first time the test project has needed
to reach an `internal` type. Every one of the 8 Identity pages injects `IdentityRedirectManager`, which is
`internal sealed` with no interface; a bUnit test needs to construct a real one (wrapping the fake
`NavigationManager` bUnit already provides) to register it for `[Inject]` to resolve at all.

## What's exempt

Two shapes of logic aren't reachable through a bUnit render, and no workaround was attempted for either:

- **A `private` `[SupplyParameterFromQuery]`/`[SupplyParameterFromForm]` property.** bUnit's parameter
  builder can only set a property a lambda expression can name, which requires at least internal
  visibility - `ResetPassword.Code`, `SetupAccount.UserId`/`Code`, and `Login.ReturnUrl` are all `private`.
  Only the branches reachable without supplying one (the missing-parameter/no-return-url paths) have
  coverage; the valid-code, valid-invite, and return-url-redirect branches do not. Making these properties
  `internal` purely for test access was considered and rejected - it would be visibility driven by
  testability rather than by the property's own design, the same reasoning ADR-0040 already applied to
  `[Inject]` property visibility.
- **A JS-interop call a test has no stake in.** Radzen components call into JS on first render for concerns
  like sizing and virtualization (e.g. `RadzenDataGrid`'s `Radzen.createDataGrid`). Any test rendering such a
  component sets `JSInterop.Mode = JSRuntimeMode.Loose`, which returns a default for any unconfigured call
  instead of throwing, rather than hand-configuring every Radzen JS call no test here actually exercises.

## Considered options

- **Test the `.razor.cs` classes directly, without a renderer** (a plain `new Dashboard()`, calling
  `OnInitializedAsync()` via a test subclass) - rejected: ADR-0040 made every `[Inject]` property `private`,
  and a subclass in a different file can't set a base class's private members. Reaching them would mean
  either reopening that visibility decision purely for testability, or reflecting into private fields, both
  worse than adopting the library built for this exact job.
- **Moq instead of NSubstitute** - equally capable for faking `UserManager<TUser>`'s virtual members; picked
  NSubstitute for its assertion syntax, no functional difference either way.

## Consequences

- `RadzenTestServices`, `ApiClientTestFactory`, `DirectorInviteServiceTestFactory`, `FakeUserManagerFactory`,
  and `IdentityTestServices` (all in `tests/VirtualLeadersGuide.Web.Tests/`) are the shared fixtures every
  component test builds on - a new component test should reach for these before writing a new one.
- `ApiClientTestFactory` also replaced `ApiEventClientShould`/`ApiDirectorClientShould`'s own previously
  near-identical private `CreateClient` helpers - it was about to become a third copy of that exact chain.
- 26 new component tests across 13 files, none duplicating what an existing HTTP-level test already covers.
