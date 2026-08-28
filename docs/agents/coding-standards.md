# Coding Standards

## XML doc comments (`///`)

Every type and every public/internal member carries standard `///` XML doc comments: `<summary>`, `<param>`,
`<returns>`, `<exception>`, `<typeparam>`, `<see cref="…"/>`/`<seealso cref="…"/>`, and `<remarks>` where a
caller genuinely needs more than a sentence. This convention was established by P2-5 (#14) — code written
before that ticket may still lack `///` comments; new and touched code should carry them.

`<summary>` states what a member *is*, normally a sentence or two; `<remarks>` carries *why*, only where a
caller genuinely needs it. Neither has a fixed length cap — a longer `<summary>` is fine when splitting it
from `<remarks>` would be artificial, and a dense `<remarks>` is fine when trimming it would lose something a
caller needs — but every line has to earn its place; padding and restating what the code already shows don't.

### Interfaces: docs live on the interface, not the implementation

Declare the contract once on the interface member. Implementing members use `<inheritdoc/>` and never restate
it — this applies to framework interfaces the same way. The canonical example is `ApiUserStore`
(`src/VirtualLeadersGuide.Web/Identity/ApiUserStore.cs`), whose ~40 members across `IUserStore<>`,
`IUserPasswordStore<>`, `IUserEmailStore<>`, `IUserSecurityStampStore<>`, `IUserLockoutStore<>` and
`IUserPhoneNumberStore<>` each take a one-line `<inheritdoc/>` inheriting ASP.NET Core's own docs, rather than
40 hand-written summaries.

Add `<remarks>` on an implementing member only where *this* implementation deviates from what the interface's
docs would lead a caller to expect — e.g. a method that reaches the network and can throw, or one that's
intentionally a no-op.

### No bare `//` rationale blocks — this reverses prior guidance

This file previously said `///` and `//` did different jobs and both should stay, with `//` header-blocks
carrying design rationale (see `ApiUserStore.cs`, `InternalAuthorizationEndpoints.cs`,
`ApiRoleGrantClient.cs` for what that looked like). That's reversed: no bare `//` rationale block survives.
`///` still carries the *contract*; anything that still needs prose is either a durable, cross-file decision
or a narrow implementation detail, and each belongs somewhere that stays discoverable and doesn't drift into
duplicates the way the old style did. See ADR-0030 for the full reasoning and rejected alternatives.

Triage each fact a `//` block used to carry individually, not the block as a whole, into one of four
destinations. A fact that clears this repo's existing ADR bar — hard to reverse, surprising without context,
and the result of a real trade-off, all three required — moves into an ADR (existing or new), and the code
keeps only a minimal `<summary>`/`<remarks>` line naming it, never a restatement. A fact that fails that bar
but is still genuinely non-obvious becomes a `<remarks>` on the member it explains. A fact an existing ADR
already covers in full keeps just the pointer. A fact that's self-evident from the code — or would be after a
clearer name — is deleted outright, with no replacement anywhere; renaming the symbol is usually the actual
fix.

Established by (#75) — code written before that ticket may still carry `//` rationale; new and touched code
should be triaged onto this convention. Commented-out code is always deleted, never left behind — git holds
the history.

### Tests

`///` goes on shared test infrastructure that other test classes consume — fixture/factory types like
`ApiWebApplicationFactory`, fakes/stubs like `StubHttpClientFactory`/`StubHttpMessageHandler` and
`FakeIdentityApiHandler`, and their public helper methods. Individual `[Fact]`/`[Theory]` test methods get
**no** `///` — ADR-0012's naming convention already makes the method name the documentation; a summary would
just restate it.

The `<remarks>` triage above extends to test *classes* that aren't shared infrastructure — a class may carry
`<remarks>` explaining non-obvious setup rationale (e.g. why a test drives `SignInManager` directly rather
than posting a rendered form). Individual `[Fact]`/`[Theory]` methods still get none. Where the same rationale
would otherwise repeat across several test classes, state it once — on shared infrastructure where one exists
— and have the others reference it with `<see cref="…"/>` rather than restating it.

No `// Arrange`/`// Act`/`// Assert` narration — ADR-0012's naming convention plus blank-line grouping
already carries the structure.

### `GenerateDocumentationFile`

Off for now. Roslyn surfaces `///` docs from source for in-solution project references, so IntelliSense works
without it. Turning it on would fire `CS1591` ("missing XML comment for publicly visible member") across every
undocumented public member that predates this convention — tracked as a follow-up to enable once the codebase
has caught up (also unblocks XML-doc-driven Swagger descriptions for P1-12, #38).

## Blazor components

A component's C# lives in a `{Component}.razor.cs` partial class, not an `@code` block, once the `.razor`
file — markup and `@code` combined — passes 40 raw physical lines. A file at 40 lines or under may keep its
`@code` inline regardless of shape; there is no additional exemption for e.g. a component with only one
`[Parameter]`, and no requirement to split out a component that sits at or under the line. The threshold is
a plain count of the file exactly as an editor's line-number gutter or `git diff` shows it — no adjustment
for blank lines, comments, or directives. See ADR-0040 for why this is a pure line count rather than a
threshold plus a list of shape-based exceptions.

Dependencies move with the code: a component with a code-behind declares them as `private [Inject]`
properties in the `.cs` file, not `@inject` directives in the markup. A component under the 40-line
threshold keeps `@inject`, matching how it already worked. `_Imports.razor`'s global `@using` list applies
only to `.razor` files — a `.razor.cs` file needs its own `using` directives for everything it references,
`Microsoft.AspNetCore.Components` (for `[Inject]`/`[Parameter]`/`[CascadingParameter]` themselves) included.

A private model class or enum used only by one component — an `InputModel`, a `PageState` enum — stays
nested inside that component's code-behind partial class rather than moving to its own file. It's an
implementation detail of one component, referenced nowhere else by name.

Custom CSS that only one component consumes goes in `{Component}.razor.css`, never in the global
`wwwroot/app.css`. `app.css` is reserved for the `--vlg-*` design-token cascade (ADR-0034), rules genuinely
shared across multiple components, and document-shell chrome with no component of its own to attach to
(`#blazor-error-ui`, whose markup lives in `App.razor`). This extends ADR-0038 rather than replacing it —
Radzen-component-first is still the first question for a layout/typography/status need; this rule only
governs where the CSS goes once ADR-0038 has already concluded custom CSS is warranted. See ADR-0040 for the
full rule, including the `SiteHeader.razor.css` reversal it documents.

Component logic that needs test coverage is tested with bUnit (ADR-0041), not by rendering the component
through a real HTTP request. `RadzenTestServices`/`ApiClientTestFactory`/`DirectorInviteServiceTestFactory`/
`FakeUserManagerFactory`/`IdentityTestServices` (all in `tests/VirtualLeadersGuide.Web.Tests/`) are this
repo's shared component-test fixtures — reach for one of these before writing a new fake from scratch. A
`private` `[SupplyParameterFromQuery]`/`[SupplyParameterFromForm]` property can't be set from a component
test at all, bUnit or otherwise, since setting it means naming it; only the branches reachable without
supplying one have coverage, and that's a known, accepted gap rather than something to work around by
loosening the property's visibility for testability alone.
