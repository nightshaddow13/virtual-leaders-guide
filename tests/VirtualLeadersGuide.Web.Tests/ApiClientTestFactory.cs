using System.Net;
using Microsoft.Extensions.Configuration;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Directors;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Builds a real <see cref="ApiEventClient"/>/<see cref="ApiDirectorClient"/> over a stubbed
/// <see cref="HttpMessageHandler"/>, for any test that needs one of this app's sealed API clients without a
/// live Api - both clients wrap the same <see cref="InternalApiClient"/>/<see cref="InternalJwtProvider"/>
/// chain, previously built independently (and identically) by <see cref="ApiEventClientShould"/> and
/// <see cref="ApiDirectorClientShould"/>; the bUnit component tests are a third consumer of that same chain.
/// </remarks>
internal static class ApiClientTestFactory
{
    private const string SigningKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";

    public static ApiEventClient CreateEventClient(HttpMessageHandler apiHandler) =>
        new(CreateInternalApiClient(apiHandler));

    public static ApiDirectorClient CreateDirectorClient(HttpMessageHandler apiHandler) =>
        new(CreateInternalApiClient(apiHandler));

    private static InternalApiClient CreateInternalApiClient(HttpMessageHandler apiHandler)
    {
        var grantsClient = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        var jwtProvider = new InternalJwtProvider(new FixedAuthenticationStateProvider("user-1"), grantsClient, Configuration());
        return new InternalApiClient(new StubHttpClientFactory(apiHandler), jwtProvider);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalJwtDefaults.SigningKeyConfigurationKey] = SigningKey
        })
        .Build();
}
