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

## Consequences

Adopting JsonApiDotNetCore (wired up in P1-6) carries two conventions every `[Resource]` entity must
follow, not chosen independently but forced by the library:

- Every resource entity inherits `Identifiable<TId>` (from `JsonApiDotNetCore.Resources`) instead of being
  a plain POCO with its own `Id` property — `IIdentifiable<TId>` is what the resource graph checks for when
  deciding whether an entity in the `DbContext` gets exposed at all. Any property meant to be visible over
  JSON:API also needs `[Attr]` — properties aren't auto-exposed.
- Resources are mounted under an `/api` namespace prefix (`JsonApiOptions.Namespace = "api"`), keeping
  generated routes (`/api/events`, `/api/pages`, etc.) separate from root-mounted endpoints like
  `/health`/`/alive`.
