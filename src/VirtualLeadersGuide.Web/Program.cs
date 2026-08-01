using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Web;
using VirtualLeadersGuide.Web.Components;
using VirtualLeadersGuide.Web.Components.Account;
using VirtualLeadersGuide.Web.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddTransient<InternalApiKeyHandler>();
builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<InternalApiKeyHandler>();

// --- Identity (P2-2, #11 - see ADR-0022) ---
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(options =>
        options.ApplicationCookie?.Configure(cookie => cookie.AccessDeniedPath = "/Account/NoAccess"));

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddUserStore<ApiUserStore>() // not AddEntityFrameworkStores - see ADR-0022
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IEmailSender<ApplicationUser>, AcsEmailSender>();

// --- Data Protection keys persisted to Blob Storage (P2-2, #11 plan section 7) ---
// vlg-web runs at min-replicas 0 (ADR-0005), so the default in-memory key ring is regenerated on every cold
// start/deploy, silently signing everyone out and invalidating outstanding password-reset links. Uses the
// Blob Storage account P1-8a already provisioned (nothing else consumes it yet). Connection-string auth,
// not a managed identity - keeps ADR-0021's "vlg-web has no managed identity" untriggered.
builder.AddAzureBlobServiceClient("blobs");
builder.Services.AddDataProtection()
    .SetApplicationName("VirtualLeadersGuide")
    .PersistKeysToAzureBlobStorage(services =>
        services.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("dataprotection-keys")
            .GetBlobClient("keys.xml"));

var app = builder.Build();

// Configure the HTTP request pipeline.
// No explicit UseAuthentication()/UseAuthorization() calls: the minimal hosting model inserts both
// automatically once authentication/authorization services are registered - verified against the installed
// .NET 10 SDK's own `dotnet new blazor -au Individual` scaffold, which doesn't call them explicitly either.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
