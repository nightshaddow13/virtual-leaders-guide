var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddAzureSqlServer("sqlserver")
    .RunAsContainer(container => container.WithDataVolume());

var database = sqlServer.AddDatabase("virtualleadersguide");

var api = builder.AddProject<Projects.VirtualLeadersGuide_Api>("api")
    .WithReference(database)
    .WaitFor(database);

if (!builder.ExecutionContext.IsPublishMode)
{
    api.WithEnvironment("Migrations__ApplyAutomatically", "true");
}

builder.AddProject<Projects.VirtualLeadersGuide_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api);

builder.Build().Run();
