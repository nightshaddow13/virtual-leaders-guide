# AppHost SDK version lives in global.json, not Directory.Packages.props

Central Package Management (`Directory.Packages.props`, `$(AspireVersion)`) governs the six `Aspire.*`
`PackageReference`s, but not `VirtualLeadersGuide.AppHost.csproj`'s `Sdk="Aspire.AppHost.Sdk"` attribute —
MSBuild resolves a project's SDK before it evaluates properties, so `$(AspireVersion)` isn't defined yet
when the SDK version is needed. We pin that version in `global.json`'s `msbuild-sdks` section instead —
the same NuGet-based SDK resolver the inline `Sdk="Name/Version"` shorthand uses, just declared once at
the repo root.

**Consequence:** the Aspire version canonically lives in two files, not one. Bumping Aspire means updating
`$(AspireVersion)` in `Directory.Packages.props` *and* the `Aspire.AppHost.Sdk` entry in `global.json`'s
`msbuild-sdks`. Forgetting the second doesn't fail the build — the AppHost SDK just silently drifts out of
lockstep with the `Aspire.Hosting.*` packages, surfacing later as inconsistent dashboard/CLI behavior
rather than a clear error. Keep the two numbers in sync by hand on every Aspire bump.
