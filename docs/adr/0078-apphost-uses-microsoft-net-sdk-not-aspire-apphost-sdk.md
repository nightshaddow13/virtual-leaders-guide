# AppHost uses Microsoft.NET.Sdk and explicit Aspire packages, not Aspire.AppHost.Sdk

Aspire 17 marks `Aspire.AppHost.Sdk` obsolete (ASPIRE012) and `AddProject<TProject>` obsolete (CS0618).
`VirtualLeadersGuide.AppHost.csproj` now uses `Microsoft.NET.Sdk` with explicit `Aspire.Hosting.AppHost`
and `Aspire.Hosting.Dotnet` package references, and `AppHost.cs` registers Api and Web with
`AddDotnetProject(name, path)` using relative paths instead of generated `Projects.*` types and
`ProjectReference`s.

**Supersedes ADR-0056.** There is no longer an SDK version in `global.json`'s `msbuild-sdks`, so the Aspire
version lives in one place, `$(AspireVersion)` in `Directory.Packages.props`, and CI's Aspire version-pair
check is removed. The EF Core / `dotnet-ef` pair (ADR-0060) is still checked.

**Consequence:** the AppHost no longer builds Api and Web through `ProjectReference`s, so building only the
AppHost doesn't build them first. `dotnet run` on the AppHost starts them through their own project paths,
and a solution build still builds everything.
