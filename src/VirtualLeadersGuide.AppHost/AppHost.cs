using Azure.Provisioning.Sql;
using Azure.Provisioning.Storage;

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

var api = builder.AddProject<Projects.VirtualLeadersGuide_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WaitFor(dataProtectionKeysContainer)
    .WithEnvironment("InternalApi__Key", internalApiKey)
    .WithEnvironment("InternalJwt__SigningKey", internalJwtKey);

var web = builder.AddProject<Projects.VirtualLeadersGuide_Web>("web")
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
}

builder.Build().Run();

/// <remarks>
/// One of two layers <c>EmailSenderRegistration</c> requires before Web will select the file sink - see
/// ADR-0032.
/// </remarks>
void AllowFileSinkEmailProvider(IResourceBuilder<ProjectResource> webBuilder) =>
    webBuilder.WithEnvironment("Email__FileSinkAllowed", "true");
