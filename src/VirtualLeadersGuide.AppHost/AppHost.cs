var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.VirtualLeadersGuide_Web>("web")
    .WithExternalHttpEndpoints();

builder.Build().Run();
