using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace VirtualLeadersGuide.Api;

public sealed class InternalApiKeyAuthenticationHandler(
    IOptionsMonitor<InternalApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<InternalApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedKey = configuration["InternalApi:Key"];
        if (string.IsNullOrEmpty(expectedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Internal API key is not configured."));
        }

        if (!Request.Headers.TryGetValue(InternalApiKeyDefaults.HeaderName, out var provided)
            || !IsMatch(provided.ToString(), expectedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing or invalid X-Internal-Key header."));
        }

        var identity = new ClaimsIdentity(Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static bool IsMatch(string provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
