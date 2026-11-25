using Aspire.Hosting.Dotnet;
using Azure.Provisioning.Sql;
using Azure.Provisioning.Storage;
using VirtualLeadersGuide.AppHost;

var portOffset = WorktreePorts.OffsetFor(
    AppContext.BaseDirectory, Environment.GetEnvironmentVariable(WorktreePorts.OverrideVariable));

ShiftDashboardPorts();

var builder = DistributedApplication.CreateBuilder(args);

var internalApiKey = builder.AddParameter("internal-api-key", secret: true);
var internalJwtKey = builder.AddParameter("internal-jwt-key", secret: true);
var acsConnectionString = builder.AddParameter("acs-connection-string", secret: true);
var adminAllowlist = builder.AddParameter("admin-allowlist", GetAdminAllowlistOrEmpty);

/// <remarks>
/// Not a secret - a list of emails, not a credential (P2-4, #13; ADR-0008). Empty fallback so a developer
/// who hasn't set this yet still gets a clean <c>dotnet run</c> (no Admins) instead of a startup failure.
/// </remarks>
string GetAdminAllowlistOrEmpty() => builder.Configuration["Parameters:admin-allowlist"] ?? string.Empty;

var sqlServer = builder.AddAzureSqlServer("sqlserver")
    .ConfigureInfrastructure(infra =>
    {
        var sqlDatabase = infra.GetProvisionableResources().OfType<SqlDatabase>().Single();

        sqlDatabase.Sku = new SqlSku
        {
            Name = "GP_S_Gen5_1",
            Tier = "GeneralPurpose",
            Family = "Gen5",
            Capacity = 1
        };
        sqlDatabase.MinCapacity = 0.5;
        sqlDatabase.AutoPauseDelay = 60;
        sqlDatabase.UseFreeLimit = true;
        sqlDatabase.FreeLimitExhaustionBehavior = FreeLimitExhaustionBehavior.AutoPause;
    })
    .RunAsContainer(container => container.WithDataVolume());

var database = sqlServer.AddDatabase("virtualleadersguide");

var storage = builder.AddAzureStorage("storage")
    .ConfigureInfrastructure(infra =>
    {
        var account = infra.GetProvisionableResources().OfType<StorageAccount>().Single();

        account.Kind = StorageKind.StorageV2;
        account.AccessTier = StorageAccountAccessTier.Hot;
        account.Sku = new StorageSku { Name = StorageSkuName.StandardLrs };
    })
    .RunAsEmulator(azurite => azurite.WithDataVolume());

var blobs = storage.AddBlobs("blobs");
var dataProtectionKeysContainer = storage.AddBlobContainer("dataprotection-keys");

var api = builder.AddDotnetProject("api", "../VirtualLeadersGuide.Api/VirtualLeadersGuide.Api.csproj")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WaitFor(dataProtectionKeysContainer)
    .WithEnvironment("InternalApi__Key", internalApiKey)
    .WithEnvironment("InternalJwt__SigningKey", internalJwtKey);

var web = builder.AddDotnetProject("web", "../VirtualLeadersGuide.Web/VirtualLeadersGuide.Web.csproj")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WaitFor(dataProtectionKeysContainer)
    .WithEnvironment("InternalApi__Key", internalApiKey)
    .WithEnvironment("InternalJwt__SigningKey", internalJwtKey)
    .WithEnvironment("Email__ConnectionString", acsConnectionString)
    .WithEnvironment("AdminAllowlist__Emails", adminAllowlist);

if (!builder.ExecutionContext.IsPublishMode)
{
    api.WithEnvironment("Migrations__ApplyAutomatically", "true");
    AllowFileSinkEmailProvider(web);

    if (portOffset != 0)
    {
        ShiftEndpointPorts(api);
        ShiftEndpointPorts(web);
        web.WithEnvironment("Cookies__NameSuffix", "." + portOffset);
    }
}

builder.Build().Run();

/// <remarks>
/// Has to run before the builder is created, which is when Aspire reads these variables.
/// </remarks>
void ShiftDashboardPorts()
{
    if (portOffset == 0)
    {
        return;
    }

    string[] variables =
        ["ASPNETCORE_URLS", "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL"];

    foreach (var variable in variables)
    {
        if (Environment.GetEnvironmentVariable(variable) is { } urls)
        {
            Environment.SetEnvironmentVariable(variable, WorktreePorts.Shift(urls, portOffset));
        }
    }
}

void ShiftEndpointPorts(IResourceBuilder<DotnetProjectResource> resource)
{
    foreach (var name in (string[])["http", "https"])
    {
        resource.WithEndpoint(name, endpoint => endpoint.Port += portOffset, createIfNotExists: false);
    }
}

/// <remarks>
/// One of two layers <c>EmailSenderRegistration</c> requires before Web will select the file sink - see
/// ADR-0032.
/// </remarks>
void AllowFileSinkEmailProvider(IResourceBuilder<DotnetProjectResource> webBuilder) =>
    webBuilder.WithEnvironment("Email__FileSinkAllowed", "true");
