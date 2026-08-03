using System.Net;
using System.Net.Http.Json;

namespace VirtualLeadersGuide.Web.Tests;

// A fake IHttpClientFactory whose "Api" client always uses the given handler and has the same BaseAddress
// shape Program.cs configures - ApiUserStore and ApiRoleGrantClient both build request URIs relative to it.
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
