# JsonApiDotNetCore for REST API generation over EF Core

We wanted a REST API generated as directly as possible from our EF Core entity models, minimizing hand-written
controller boilerplate. We evaluated OData for ASP.NET Core (still needs a controller class per entity set,
though it gets $filter/$select/$expand querying) and `dotnet-aspnet-codegenerator` scaffolding (one-time
boilerplate you then own and maintain by hand) against **JsonApiDotNetCore**, and chose JsonApiDotNetCore: putting
`[Resource]` on an EF Core entity yields full CRUD plus filter/sort/page/include support with no hand-written
controllers at all.

## Considered options

- OData for ASP.NET Core — rejected as still requiring a controller + EDM model per entity set.
- `dotnet-aspnet-codegenerator` scaffolding — rejected as a one-time generator producing code you then maintain
  by hand, the opposite of "auto-generated."
- JsonApiDotNetCore — chosen for being the most declarative, model-driven option.
