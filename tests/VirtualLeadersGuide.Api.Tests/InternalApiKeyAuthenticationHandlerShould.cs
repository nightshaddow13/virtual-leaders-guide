using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualLeadersGuide.Api;

namespace VirtualLeadersGuide.Api.Tests;

public class InternalApiKeyAuthenticationHandlerShould
{
    private const string ConfiguredKey = "expected-key";

    [Fact]
    public async Task SucceedAuthentication_WhenXInternalKeyHeaderIsCorrect_ForAuthenticateAsync()
    {
        var result = await AuthenticateAsync(configuredKey: ConfiguredKey, providedKey: ConfiguredKey);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task FailAuthentication_WhenXInternalKeyHeaderIsMissing_ForAuthenticateAsync()
    {
        var result = await AuthenticateAsync(configuredKey: ConfiguredKey, providedKey: null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FailAuthentication_WhenXInternalKeyHeaderIsWrong_ForAuthenticateAsync()
    {
        var result = await AuthenticateAsync(configuredKey: ConfiguredKey, providedKey: "wrong-key");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FailAuthentication_WhenConfiguredKeyIsMissing_ForAuthenticateAsync()
    {
        var result = await AuthenticateAsync(configuredKey: null, providedKey: ConfiguredKey);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SetStatusCode401_ForChallengeAsync()
    {
        var (handler, httpContext) = await CreateInitializedHandlerAsync(configuredKey: ConfiguredKey);

        await handler.ChallengeAsync(properties: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(string? configuredKey, string? providedKey)
    {
        var (handler, _) = await CreateInitializedHandlerAsync(configuredKey, providedKey);
        return await handler.AuthenticateAsync();
    }

    private static async Task<(InternalApiKeyAuthenticationHandler Handler, HttpContext HttpContext)> CreateInitializedHandlerAsync(
        string? configuredKey, string? providedKey = null)
    {
        var configurationData = new Dictionary<string, string?>();
        if (configuredKey is not null)
        {
            configurationData["InternalApi:Key"] = configuredKey;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var httpContext = new DefaultHttpContext();
        if (providedKey is not null)
        {
            httpContext.Request.Headers[InternalApiKeyDefaults.HeaderName] = providedKey;
        }

        var handler = new InternalApiKeyAuthenticationHandler(
            new TestOptionsMonitor(),
            LoggerFactory.Create(_ => { }),
            UrlEncoder.Default,
            configuration);

        var scheme = new AuthenticationScheme(
            InternalApiKeyDefaults.AuthenticationScheme, null, typeof(InternalApiKeyAuthenticationHandler));

        await handler.InitializeAsync(scheme, httpContext);

        return (handler, httpContext);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<InternalApiKeyAuthenticationOptions>
    {
        public InternalApiKeyAuthenticationOptions CurrentValue { get; } = new();

        public InternalApiKeyAuthenticationOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<InternalApiKeyAuthenticationOptions, string?> listener) => null;
    }
}
