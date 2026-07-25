# .NET 10 LTS + Aspire + Blazor + EF Core as the base stack

The platform needs an admin dashboard, a data-backed API, and a public-facing site, built by someone already
comfortable with .NET and SQL Server. We chose **.NET 10 (LTS)** as the target framework — the current
long-term-support release, minimizing how often a personal project is forced into a framework upgrade just to
stay supported. On top of that, **.NET Aspire** orchestrates the solution (service discovery, local dev dashboard,
first-class Azure deployment tooling), **Blazor Web App** (Interactive Server) is the frontend, a separate
**ASP.NET Core Web API** project is the backend, and **EF Core** is the data-access layer against Azure SQL.
