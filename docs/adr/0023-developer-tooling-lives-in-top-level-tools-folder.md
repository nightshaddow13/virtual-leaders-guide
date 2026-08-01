# Developer tooling lives in a top-level `tools/` folder, separate from `src/` and `tests/`

Adding `SeedUser` (a checked-in console tool for creating a local-Identity account without a Register page or
seeding code in the app - see the P2-2 (#11) plan's Scope section for why the app itself doesn't do this)
raised the same "what folder does this go in" question ADR-0010/0011/0012 answered for other first
instances. We decided on a top-level `tools/` folder, one project per tool, mirroring `src/`/`tests/`'s
existing split: `src/` is what ships, `tests/` is what verifies it ships (ADR-0011), and `tools/` is what
helps a developer operate the app locally without being either. Tool projects are added to the `.slnx` for
discoverability but are never referenced by `AppHost.cs` (so they don't run as part of the orchestrated app)
and set no `ContainerRepository` (so they're never publishable as a container image - moot anyway, since
`build.yml`'s publish loop is hardcoded to `Api`/`Web` by name).

## Considered options

- **Put it under `src/`**, e.g. `src/VirtualLeadersGuide.Tools.SeedUser` - keeps one fewer top-level folder,
  but blurs `src/`'s "what ships" meaning (ADR-0011) the same way colocating tests there would have.
- **A loose `scripts/` folder with an untyped script** (PowerShell, a `.csx` file) - no project/csproj
  ceremony, but gives up compile-time checking against `VirtualLeadersGuide.Identity.Contracts` (the DTO
  `SeedUser` sends is exactly the wire shape `ApiUserStore`/`InternalIdentityEndpoints` already share -
  losing that reference is what the runbook-embedded throwaway version of this tool did, and is exactly the
  gap this ADR's decision closes).

## Consequences

Every future one-off dev/ops tool (there is currently only one) has an obvious home and naming pattern
(`VirtualLeadersGuide.Tools.<Name>`) rather than a repeat of this decision.
