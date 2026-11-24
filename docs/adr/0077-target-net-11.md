# Target .NET 11 instead of .NET 10 LTS

ADR-0001 chose .NET 10 (LTS) as the target framework so a personal project would rarely be forced into a
framework upgrade. We are moving every project to **.NET 11** (`net11.0`) instead. The Aspire + Blazor +
EF Core stack from ADR-0001 is unchanged; only the framework version moves.

The immediate trigger was tooling: the development machine only has SDK 11.0.100, and `global.json`'s
`rollForward: latestFeature` can't roll a 10.0.x pin up to a new major, so every build failed with
NETSDK1141. Keeping .NET 10 would have meant maintaining a second SDK install just to hold the old target.

The cost of leaving LTS is smaller than ADR-0001 assumed. .NET 11 is an STS release, but STS support is now
24 months (Nov 2026 to about Nov 2028), which is about as long as .NET 10 LTS has left. The next upgrade
(.NET 12 LTS) is therefore due by about Nov 2028 either way.

`global.json` now pins SDK `11.0.100`, CI's `setup-dotnet` uses `11.0.x`, `dotnet-ef` is `11.0.0`, and the
migrator image uses `runtime-deps:11.0`. `Microsoft.IdentityModel.JsonWebTokens` stays held at 8.19.2
(ADR-0060): `Microsoft.AspNetCore.Authentication.JwtBearer` 11.0.0 still pulls 8.19.2 transitively.
Aspire moves 13.5.3 -> 17.0.0 in the same change (see ADR-0078); no Aspire release targets `net11.0` yet, but
its `net10.0` assemblies run on .NET 11.

Supersedes ADR-0001's choice of .NET 10 as the target framework.
