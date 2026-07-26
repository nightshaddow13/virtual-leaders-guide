using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<VirtualLeadersGuideDbContext>("virtualleadersguide");

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Migrations:ApplyAutomatically"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>()
        .Database.Migrate();
}

app.MapDefaultEndpoints();

app.Run();
