using System.Net;
using Microsoft.Extensions.Configuration;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class InternalApiClientShould
{
    private const string SigningKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task AttachTheBearerTokenFromInternalJwtProvider_WhenSendingARequest_ForSendAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var apiHandler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var grantsClient = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        var jwtProvider = new InternalJwtProvider(
            new FixedAuthenticationStateProvider("user-1"), grantsClient, Configuration());
        var apiClient = new InternalApiClient(new StubHttpClientFactory(apiHandler), jwtProvider);

        string expectedToken = await jwtProvider.GetTokenAsync(CancellationToken.None);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        await apiClient.SendAsync(request, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest!.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal(expectedToken, capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task NotAttachAnAuthorizationHeader_WhenApiUserStoreSendsARequest_ForFindByIdAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var store = new ApiUserStore(new StubHttpClientFactory(handler));

        await store.FindByIdAsync("user-1", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest!.Headers.Authorization);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalJwtDefaults.SigningKeyConfigurationKey] = SigningKey
        })
        .Build();
}
