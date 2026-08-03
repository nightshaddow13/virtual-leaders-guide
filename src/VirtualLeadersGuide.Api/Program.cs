using JsonApiDotNetCore.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Api.Identity;

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

builder.Services.AddJsonApi<VirtualLeadersGuideDbContext>(options =>
{
    options.Namespace = "api";
});

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

if (builder.Configuration.GetValue<bool>("Migrations:ApplyAutomatically"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>()
        .Database.Migrate();
}

app.UseJsonApi();
app.MapControllers();
app.MapInternalIdentityEndpoints();
app.MapInternalAuthorizationEndpoints();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
