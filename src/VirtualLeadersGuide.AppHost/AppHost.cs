using Azure.Provisioning.Sql;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);

var internalApiKey = builder.AddParameter("internal-api-key", secret: true);
// Signs/validates the internal JWT (P2-5, #14; ADR-0007) - separate from internal-api-key since the two
// answer different trust questions and should rotate independently.
var internalJwtKey = builder.AddParameter("internal-jwt-key", secret: true);
var acsConnectionString = builder.AddParameter("acs-connection-string", secret: true);

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
    .WithEnvironment("InternalApi__Key", internalApiKey)
    .WithEnvironment("InternalJwt__SigningKey", internalJwtKey);

if (!builder.ExecutionContext.IsPublishMode)
{
    api.WithEnvironment("Migrations__ApplyAutomatically", "true");
}

builder.AddProject<Projects.VirtualLeadersGuide_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WaitFor(dataProtectionKeysContainer)
    .WithEnvironment("InternalApi__Key", internalApiKey)
    .WithEnvironment("InternalJwt__SigningKey", internalJwtKey)
    .WithEnvironment("Email__ConnectionString", acsConnectionString);

builder.Build().Run();
