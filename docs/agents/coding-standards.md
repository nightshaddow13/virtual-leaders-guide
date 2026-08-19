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
