using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Directors;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Exercises <see cref="DirectorInviteService"/> against a real <see cref="UserManager{TUser}"/>/
/// <see cref="ApiUserStore"/> (backed by <see cref="FakeIdentityApiHandler"/>, extended for this ticket
/// with Create/Delete) and a real <see cref="ApiDirectorClient"/> (backed by
/// <see cref="StubHttpMessageHandler"/>, same pattern as <c>ApiDirectorClientShould</c>) - no mocking
/// library in this repo, so the real collaborators are built from real, if minimal, DI wiring rather than
/// faked interfaces.
/// </remarks>
public class DirectorInviteServiceShould
{
    private const string SigningKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task ReturnNewEmail_WhenNoUserExists_ForLookUpAsync()
    {
        using Fixture fixture = Fixture.Create();

        InviteLookup lookup = await fixture.Service.LookUpAsync("nobody@example.com", CancellationToken.None);

        Assert.False(lookup.IsExistingUser);
        Assert.Null(lookup.ExistingUser);
    }

    [Fact]
    public async Task ReturnExistingUser_WhenAUserAlreadyExists_ForLookUpAsync()
    {
        using Fixture fixture = Fixture.Create();
        IdentityUserDto seeded = SeedUser("jo@pack44.org", hasPassword: true);
        fixture.Identity.Seed(seeded);
        fixture.RoleGrantsResponder = request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == $"/api/users/{seeded.Id}")
            {
                return JsonApiResponse(HttpStatusCode.OK, new
                {
                    data = new { type = "users", id = seeded.Id, attributes = new { email = seeded.Email, displayName = (string?)null, hasCredential = true } }
                });
            }

            return path == "/api/roleGrants" ? JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() }) : null;
        };

        InviteLookup lookup = await fixture.Service.LookUpAsync("jo@pack44.org", CancellationToken.None);

        Assert.True(lookup.IsExistingUser);
        Assert.Equal("jo@pack44.org", lookup.ExistingUser?.Email);
    }

    [Fact]
    public async Task CreateAPasswordlessUserAndSendAnInvite_WhenTheEmailIsNew_ForInviteAsync()
    {
        using Fixture fixture = Fixture.Create();

        InviteOutcome outcome = await fixture.Service.InviteAsync(
            "dana@troop7.org", "Dana Okafor", CancellationToken.None);

        Assert.Equal(InviteOutcome.Invited, outcome);

        ApplicationUser? created = await fixture.UserManager.FindByEmailAsync("dana@troop7.org");
        Assert.NotNull(created);
        Assert.Equal("Dana Okafor", created!.DisplayName);
        Assert.False(await fixture.UserManager.HasPasswordAsync(created));

        Assert.Single(fixture.EmailSender.Sent);
        Assert.Equal("dana@troop7.org", fixture.EmailSender.Sent[0].Email);
        Assert.Contains("/setup?", fixture.EmailSender.Sent[0].SetupLink, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnAlreadyOnPlatform_WithoutSendingAnEmail_WhenTheEmailAlreadyExists_ForInviteAsync()
    {
        using Fixture fixture = Fixture.Create();
        fixture.Identity.Seed(SeedUser("jo@pack44.org", hasPassword: true));

        InviteOutcome outcome = await fixture.Service.InviteAsync("jo@pack44.org", null, CancellationToken.None);

        Assert.Equal(InviteOutcome.AlreadyOnPlatform, outcome);
        Assert.Empty(fixture.EmailSender.Sent);
    }

    [Fact]
    public async Task RollBackTheCreatedUser_WhenGrantingTheRoleFails_ForInviteAsync()
    {
        using Fixture fixture = Fixture.Create();
        fixture.RoleGrantsResponder = request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;

        InviteOutcome outcome = await fixture.Service.InviteAsync("dana@troop7.org", null, CancellationToken.None);

        Assert.Equal(InviteOutcome.StoreUnavailable, outcome);
        Assert.Null(await fixture.UserManager.FindByEmailAsync("dana@troop7.org"));
        Assert.Empty(fixture.EmailSender.Sent);
    }

    [Fact]
    public async Task ReturnSentAndRotateTheSecurityStamp_ForResendAsync()
    {
        using Fixture fixture = Fixture.Create();
        IdentityUserDto user = SeedUser("dana@troop7.org", hasPassword: false);
        fixture.Identity.Seed(user);
        string originalStamp = user.SecurityStamp!;

        ResendOutcome outcome = await fixture.Service.ResendAsync(user.Id, CancellationToken.None);

        Assert.Equal(ResendOutcome.Sent, outcome);
        Assert.Single(fixture.EmailSender.Sent);

        ApplicationUser? reloaded = await fixture.UserManager.FindByIdAsync(user.Id);
        Assert.NotEqual(originalStamp, reloaded!.SecurityStamp);
    }

    [Fact]
    public async Task ReturnAlreadyActive_WhenTheUserAlreadyHasAPassword_ForResendAsync()
    {
        using Fixture fixture = Fixture.Create();
        IdentityUserDto user = SeedUser("jo@pack44.org", hasPassword: true);
        fixture.Identity.Seed(user);

        ResendOutcome outcome = await fixture.Service.ResendAsync(user.Id, CancellationToken.None);

        Assert.Equal(ResendOutcome.AlreadyActive, outcome);
        Assert.Empty(fixture.EmailSender.Sent);
    }

    [Fact]
    public async Task ReturnNotFound_ForResendAsync()
    {
        using Fixture fixture = Fixture.Create();

        ResendOutcome outcome = await fixture.Service.ResendAsync("missing", CancellationToken.None);

        Assert.Equal(ResendOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task ReturnRevokedAndDeleteTheUser_ForRevokeAsync()
    {
        using Fixture fixture = Fixture.Create();
        IdentityUserDto user = SeedUser("dana@troop7.org", hasPassword: false);
        fixture.Identity.Seed(user);

        RevokeOutcome outcome = await fixture.Service.RevokeAsync(user.Id, CancellationToken.None);

        Assert.Equal(RevokeOutcome.Revoked, outcome);
        Assert.False(fixture.Identity.Contains(user.Id));
    }

    [Fact]
    public async Task ReturnAlreadyActive_WhenTheUserAlreadyHasAPassword_ForRevokeAsync()
    {
        using Fixture fixture = Fixture.Create();
        IdentityUserDto user = SeedUser("jo@pack44.org", hasPassword: true);
        fixture.Identity.Seed(user);

        RevokeOutcome outcome = await fixture.Service.RevokeAsync(user.Id, CancellationToken.None);

        Assert.Equal(RevokeOutcome.AlreadyActive, outcome);
        Assert.True(fixture.Identity.Contains(user.Id));
    }

    private static IdentityUserDto SeedUser(string email, bool hasPassword) => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = hasPassword,
        PasswordHash = hasPassword ? new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "P@ssw0rd123!") : null,
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString(),
        LockoutEnabled = true
    };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return response;
    }

    /// <remarks>
    /// Bundles the real collaborators <see cref="DirectorInviteService"/> needs. Two independent fake "Api"
    /// backends - one for <c>/internal/identity/*</c> (<see cref="ApiUserStore"/>'s traffic), one for
    /// <c>/api/*</c> (<see cref="ApiDirectorClient"/>'s, via <see cref="InternalApiClient"/>) - which is
    /// looser than production's single shared <c>"Api"</c> named client, but adequate here: this fixture
    /// tests <see cref="DirectorInviteService"/>'s own orchestration, not whether its two collaborators
    /// share a socket.
    /// </remarks>
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _identityProvider;

        public FakeIdentityApiHandler Identity { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public FakeInviteEmailSender EmailSender { get; }
        public DirectorInviteService Service { get; }

        /// <summary>
        /// When set, intercepts <c>/api/*</c> requests before the default (always-succeeds) responder -
        /// return <see langword="null"/> to fall through to the default.
        /// </summary>
        public Func<HttpRequestMessage, HttpResponseMessage?>? RoleGrantsResponder { get; set; }

        private Fixture(ServiceProvider identityProvider, FakeIdentityApiHandler identity,
            UserManager<ApplicationUser> userManager, FakeInviteEmailSender emailSender, DirectorInviteService service)
        {
            _identityProvider = identityProvider;
            Identity = identity;
            UserManager = userManager;
            EmailSender = emailSender;
            Service = service;
        }

        public static Fixture Create()
        {
            var identity = new FakeIdentityApiHandler();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(identity));
            services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddUserStore<ApiUserStore>()
                .AddDefaultTokenProviders()
                .AddTokenProvider<InviteTokenProvider>("Invite");

            ServiceProvider provider = services.BuildServiceProvider();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

            Fixture? fixture = null;
            var apiHandler = new StubHttpMessageHandler(request =>
                fixture!.RoleGrantsResponder?.Invoke(request) ?? DefaultApiResponse(request));

            var grantsClient = new ApiRoleGrantClient(new StubHttpClientFactory(
                StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
            var jwtProvider = new InternalJwtProvider(
                new FixedAuthenticationStateProvider("admin-1"), grantsClient, Configuration());
            var internalApiClient = new InternalApiClient(new StubHttpClientFactory(apiHandler), jwtProvider);
            var directorClient = new ApiDirectorClient(internalApiClient);

            var emailSender = new FakeInviteEmailSender();
            var navigationManager = new TestNavigationManager("https://vlg.example/");

            var service = new DirectorInviteService(
                userManager, directorClient, emailSender, navigationManager, NullLogger<DirectorInviteService>.Instance);

            fixture = new Fixture(provider, identity, userManager, emailSender, service);
            return fixture;
        }

        /// <remarks>
        /// A grant POST succeeds (201) by default, and any GET against <c>/api/users</c>/<c>/api/roleGrants</c>
        /// (e.g. <see cref="DirectorInviteService.LookUpAsync"/>'s existing-user join) returns an empty
        /// collection - individual tests override specific responses via <see cref="RoleGrantsResponder"/>.
        /// </remarks>
        private static HttpResponseMessage DefaultApiResponse(HttpRequestMessage request)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/roleGrants")
            {
                return JsonApiResponse(HttpStatusCode.Created, new
                {
                    data = new { type = "roleGrants", id = Guid.NewGuid().ToString(), attributes = new { } }
                });
            }

            return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        }

        public void Dispose() => _identityProvider.Dispose();
    }

    private sealed class FakeInviteEmailSender : IInviteEmailSender
    {
        public List<(string Email, string SetupLink)> Sent { get; } = [];

        public Task SendDirectorInviteAsync(ApplicationUser user, string email, string setupLink)
        {
            Sent.Add((email, setupLink));
            return Task.CompletedTask;
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri) => Initialize(baseUri, baseUri);

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            throw new NotSupportedException("DirectorInviteService never calls NavigateTo.");
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalJwtDefaults.SigningKeyConfigurationKey] = SigningKey
        })
        .Build();
}
