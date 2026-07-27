using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualLeadersGuide.Api;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string InternalApiKey = "test-internal-api-key";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalApi:Key"] = InternalApiKey
            });
        });

        builder.ConfigureServices(services =>
        {
            // Aspire's AddSqlServerDbContext pools contexts (AddDbContextPool), registering
            // DbContextOptions<T> as singleton plus internal pool-lease services closed over T.
            // Removing only DbContextOptions<T> leaves those singletons behind expecting a pooled
            // registration, so every descriptor closed over the DbContext type must go before
            // re-adding a plain (non-pooled) AddDbContext for SQLite (ADR-0014).
            services.RemoveAll<VirtualLeadersGuideDbContext>();

            foreach (ServiceDescriptor descriptor in services
                .Where(descriptor => descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericArguments().Contains(typeof(VirtualLeadersGuideDbContext)))
                .ToList())
            {
                services.Remove(descriptor);
            }

            _connection.Open();

            services.AddDbContext<VirtualLeadersGuideDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyDefaults.HeaderName, InternalApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}
