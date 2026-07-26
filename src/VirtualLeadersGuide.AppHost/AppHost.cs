var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.VirtualLeadersGuide_Api>("api");

builder.AddProject<Projects.VirtualLeadersGuide_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api);

builder.Build().Run();
