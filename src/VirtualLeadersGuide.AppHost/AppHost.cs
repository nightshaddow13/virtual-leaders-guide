var builder = DistributedApplication.CreateBuilder(args);

var internalApiKey = builder.AddParameter("internal-api-key", secret: true);

var sqlServer = builder.AddAzureSqlServer("sqlserver")
    .RunAsContainer(container => container.WithDataVolume());

var database = sqlServer.AddDatabase("virtualleadersguide");

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
