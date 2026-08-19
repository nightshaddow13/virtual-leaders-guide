using System.Net;
using System.Net.Http.Json;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// A fake <see cref="IHttpClientFactory"/> whose <c>"Api"</c> client always uses the given handler and has
/// the same <c>BaseAddress</c> shape <c>Program.cs</c> configures - <c>ApiUserStore</c> and
/// <c>ApiRoleGrantClient</c> both build request URIs relative to it.
/// </remarks>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.internal/") };
}

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public static StubHttpMessageHandler RespondingWith(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));

    public static StubHttpMessageHandler RespondingWithJson<T>(HttpStatusCode statusCode, T body) =>
        new(_ => new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) });

    public static StubHttpMessageHandler ThrowingOn(Func<Exception> exceptionFactory) =>
        new(_ => throw exceptionFactory());

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
