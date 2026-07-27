using Azure.Provisioning.Sql;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);

var internalApiKey = builder.AddParameter("internal-api-key", secret: true);

builder.AddAzureContainerAppEnvironment("cae");

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

storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.VirtualLeadersGuide_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("InternalApi__Key", internalApiKey);

if (!builder.ExecutionContext.IsPublishMode)
{
    api.WithEnvironment("Migrations__ApplyAutomatically", "true");
}

builder.AddProject<Projects.VirtualLeadersGuide_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("InternalApi__Key", internalApiKey);

builder.Build().Run();
