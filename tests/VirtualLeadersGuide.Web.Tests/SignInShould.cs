using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

// Exercises real DI wiring - AddIdentityCore<ApplicationUser>().AddUserStore<ApiUserStore>() plus
// SignInManager - against a fake in-memory "Api" backend, proving our own wiring cooperates correctly with
// the framework. Drives SignInManager directly against a synthetic HttpContext rather than posting the
// actual rendered Login.razor form: the form's wire format (antiforgery token, Blazor's named-form hidden
// fields) is framework-templated markup this ticket didn't write, so testing it would mostly be testing
// Blazor itself. What this ticket owns - ApiUserStore correctly backing UserManager/SignInManager end to
// end - is exactly what this test exercises.
public class SignInShould : IAsyncLifetime
{
    private const string KnownEmail = "director@example.com";
    private const string KnownPassword = "P@ssw0rd123!";

    private WebApplicationFactory<Program> _factory = null!;
    private FakeIdentityApiHandler _fakeApi = null!;
    private readonly string _dataProtectionKeysDirectory =
        Path.Combine(Path.GetTempPath(), "vlg-web-tests-keys-" + Guid.NewGuid());

    public Task InitializeAsync()
    {
        _fakeApi = new FakeIdentityApiHandler();

        var passwordHash = new PasswordHasher<ApplicationUser>()
            .HashPassword(new ApplicationUser(), KnownPassword);
        _fakeApi.Seed(new IdentityUserDto
        {
            Id = Guid.NewGuid().ToString(),
            UserName = KnownEmail,
            NormalizedUserName = KnownEmail.ToUpperInvariant(),
            Email = KnownEmail,
            NormalizedEmail = KnownEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            PasswordHash = passwordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        });

        // Program.cs unconditionally registers ConnectionStrings:blobs (AddAzureBlobServiceClient) as part
        // of top-level statement execution, before WebApplicationFactory's ConfigureAppConfiguration hook
        // for deferred/minimal-hosting apps has a chance to apply - an environment variable is read by
        // WebApplication.CreateBuilder's own default configuration sources from the very first line, so it
        // sidesteps that ordering question entirely. The value itself is never actually dialed: Data
        // Protection persistence is redirected to a local temp directory below before anything touches it.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__blobs",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replaces the transport under the "Api" named HttpClient Program.cs already registers -
                // InternalApiKeyHandler still runs (harmlessly), it just delegates to this fake instead of
                // a real network call.
                services.AddHttpClient("Api").ConfigurePrimaryHttpMessageHandler(() => _fakeApi);

                services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_dataProtectionKeysDirectory));
            });
        });

        // Forces the host to build now, while the env var above is still set, rather than lazily on first
        // use - see DisposeAsync.
        _ = _factory.Services;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__blobs", null);

        if (Directory.Exists(_dataProtectionKeysDirectory))
        {
            Directory.Delete(_dataProtectionKeysDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SucceedAndIssueTheApplicationCookie_WhenCredentialsAreValid_ForPasswordSignInAsync()
    {
        (SignInResult result, DefaultHttpContext httpContext) = await SignInAsync(KnownEmail, KnownPassword);

        Assert.True(result.Succeeded);
        Assert.Contains(
            httpContext.Response.Headers.SetCookie,
            value => value?.Contains(".AspNetCore.Identity.Application", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task FailWithNoCookieIssued_WhenThePasswordIsWrong_ForPasswordSignInAsync()
    {
        (SignInResult result, DefaultHttpContext httpContext) = await SignInAsync(KnownEmail, "wrong-password");

        Assert.False(result.Succeeded);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task FailWithNoCookieIssued_WhenNoAccountExistsForTheEmail_ForPasswordSignInAsync()
    {
        (SignInResult result, DefaultHttpContext httpContext) =
            await SignInAsync("nobody@example.com", KnownPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    private async Task<(SignInResult Result, DefaultHttpContext HttpContext)> SignInAsync(string email, string password)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        signInManager.Context = httpContext;

        SignInResult result = await signInManager.PasswordSignInAsync(
            email, password, isPersistent: false, lockoutOnFailure: false);

        return (result, httpContext);
    }
}

// A tiny in-memory identity store speaking the same wire shape as InternalIdentityEndpoints (Api), keyed
// by id, with normalized-name/email lookups - just enough of the real endpoint surface for
// UserManager/SignInManager to complete a password sign-in against.
internal sealed class FakeIdentityApiHandler : HttpMessageHandler
{
    private readonly Dictionary<string, IdentityUserDto> _usersById = [];

    public void Seed(IdentityUserDto user) => _usersById[user.Id] = user;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Get)
        {
            const string byNamePrefix = "/internal/identity/users/by-name/";
            const string byEmailPrefix = "/internal/identity/users/by-email/";
            const string byIdPrefix = "/internal/identity/users/";

            if (path.StartsWith(byNamePrefix, StringComparison.Ordinal))
            {
                string normalizedUserName = Uri.UnescapeDataString(path[byNamePrefix.Length..]);
                return Task.FromResult(FindOrNotFound(u => u.NormalizedUserName == normalizedUserName));
            }

            if (path.StartsWith(byEmailPrefix, StringComparison.Ordinal))
            {
                string normalizedEmail = Uri.UnescapeDataString(path[byEmailPrefix.Length..]);
                return Task.FromResult(FindOrNotFound(u => u.NormalizedEmail == normalizedEmail));
            }

            if (path.StartsWith(byIdPrefix, StringComparison.Ordinal))
            {
                string id = Uri.UnescapeDataString(path[byIdPrefix.Length..]);
                return Task.FromResult(_usersById.TryGetValue(id, out IdentityUserDto? user)
                    ? JsonResponse(HttpStatusCode.OK, user)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        if (request.Method == HttpMethod.Put && path.StartsWith("/internal/identity/users/", StringComparison.Ordinal))
        {
            var updated = request.Content!.ReadFromJsonAsync<IdentityUserDto>(cancellationToken)
                .GetAwaiter().GetResult()!;
            updated.ConcurrencyStamp = Guid.NewGuid().ToString();
            _usersById[updated.Id] = updated;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, updated));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage FindOrNotFound(Func<IdentityUserDto, bool> predicate)
    {
        IdentityUserDto? match = _usersById.Values.FirstOrDefault(predicate);
        return match is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(HttpStatusCode.OK, match);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, IdentityUserDto body) =>
        new(statusCode) { Content = JsonContent.Create(body) };
}
