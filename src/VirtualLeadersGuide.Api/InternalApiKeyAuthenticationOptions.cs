using Microsoft.AspNetCore.Authentication;

namespace VirtualLeadersGuide.Api;

public sealed class InternalApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

public static class InternalApiKeyDefaults
{
    public const string AuthenticationScheme = "InternalApiKey";
    public const string HeaderName = "X-Internal-Key";
}
