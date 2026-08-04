# Coding Standards

## XML doc comments (`///`)

Every type and every public/internal member carries standard `///` XML doc comments: `<summary>`, `<param>`,
`<returns>`, `<exception>`, `<typeparam>`, `<see cref="…"/>`/`<seealso cref="…"/>`, and `<remarks>` where a
caller genuinely needs more than a sentence. This convention was established by P2-5 (#14) — code written
before that ticket may still lack `///` comments; new and touched code should carry them.

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

### `///` and `//` do different jobs — both stay

`///` carries the *contract*: what a member does, what to pass, what comes back, what it throws. The
pre-existing `//` header-block style carries *rationale*: why this design over the obvious one (see
`ApiUserStore.cs`, `InternalAuthorizationEndpoints.cs`, `ApiRoleGrantClient.cs` for examples). Rationale is
not migrated into `<remarks>` — it's valuable in-file, but would bloat every hover tooltip. Add `///`
alongside existing `//` rationale; don't replace it.

### Tests

`///` goes on shared test infrastructure that other test classes consume — fixture/factory types like
`ApiWebApplicationFactory`, fakes/stubs like `StubHttpClientFactory`/`StubHttpMessageHandler` and
`FakeIdentityApiHandler`, and their public helper methods. Individual `[Fact]`/`[Theory]` test methods get
**no** `///` — ADR-0012's naming convention already makes the method name the documentation; a summary would
just restate it.

### `GenerateDocumentationFile`

Off for now. Roslyn surfaces `///` docs from source for in-solution project references, so IntelliSense works
without it. Turning it on would fire `CS1591` ("missing XML comment for publicly visible member") across every
undocumented public member that predates this convention — tracked as a follow-up to enable once the codebase
has caught up (also unblocks XML-doc-driven Swagger descriptions for P1-12, #38).
