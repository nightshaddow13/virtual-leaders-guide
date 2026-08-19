using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Web;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components;
using VirtualLeadersGuide.Web.Components.Account;
using VirtualLeadersGuide.Web.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddTransient<InternalApiKeyHandler>();
builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<InternalApiKeyHandler>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(options =>
        options.ApplicationCookie?.Configure(cookie => cookie.AccessDeniedPath = "/Account/NoAccess"));

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddUserStore<ApiUserStore>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

builder.Services.AddScoped<IEmailSender<ApplicationUser>, AcsEmailSender>();

builder.Services.AddScoped<ApiRoleGrantClient>();

builder.Services.Configure<AdminAllowlistOptions>(
    builder.Configuration.GetSection(AdminAllowlistOptions.SectionName));
builder.Services.AddScoped<AdminAllowlistSynchronizer>();

builder.Services.AddScoped<InternalJwtProvider>();
builder.Services.AddScoped<InternalApiClient>();

AddWebDataProtection(builder);

var app = builder.Build();

ConfigureHttpPipeline(app);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapDefaultEndpoints();

app.Run();

/// <remarks>
/// See <c>docs/runbooks/p2-2-blob-dataprotection-keys.md</c> and ADR-0021 for why this is needed and why
/// connection-string, not managed identity.
/// </remarks>
void AddWebDataProtection(WebApplicationBuilder webBuilder)
{
    webBuilder.AddAzureBlobServiceClient("blobs");
    webBuilder.Services.AddDataProtection()
        .SetApplicationName("VirtualLeadersGuide")
        .PersistKeysToAzureBlobStorage(services =>
            services.GetRequiredService<BlobServiceClient>()
                .GetBlobContainerClient("dataprotection-keys")
                .GetBlobClient("keys.xml"));
}

/// <remarks>
/// No explicit <c>UseAuthentication()</c>/<c>UseAuthorization()</c> calls: the minimal hosting model inserts
/// both automatically once authentication/authorization services are registered - verified against the
/// installed .NET 10 SDK's own <c>dotnet new blazor -au Individual</c> scaffold, which doesn't call them
/// explicitly either.
/// </remarks>
void ConfigureHttpPipeline(WebApplication webApp)
{
    if (!webApp.Environment.IsDevelopment())
    {
        webApp.UseExceptionHandler("/Error", createScopeForErrors: true);
        webApp.UseHsts();
    }

    webApp.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    webApp.UseHttpsRedirection();
    webApp.UseAntiforgery();
}

public partial class Program;
