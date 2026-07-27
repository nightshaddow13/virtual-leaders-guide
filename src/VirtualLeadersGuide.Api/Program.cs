using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api;
using VirtualLeadersGuide.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<VirtualLeadersGuideDbContext>("virtualleadersguide");

builder.Services.AddAuthentication(InternalApiKeyDefaults.AuthenticationScheme)
    .AddScheme<InternalApiKeyAuthenticationOptions, InternalApiKeyAuthenticationHandler>(
        InternalApiKeyDefaults.AuthenticationScheme, options => { });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (builder.Configuration.GetValue<bool>("Migrations:ApplyAutomatically"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>()
        .Database.Migrate();
}

app.MapDefaultEndpoints();

app.Run();
