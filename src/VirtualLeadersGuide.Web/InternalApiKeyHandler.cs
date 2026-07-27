namespace VirtualLeadersGuide.Web;

public sealed class InternalApiKeyHandler(IConfiguration configuration) : DelegatingHandler
{
    private const string HeaderName = "X-Internal-Key";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = configuration["InternalApi:Key"];
        if (!string.IsNullOrEmpty(key))
        {
            request.Headers.Add(HeaderName, key);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
