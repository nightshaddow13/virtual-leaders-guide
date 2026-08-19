using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive counterpart to <c>IdentityEntitiesAreNotJsonApiResourcesShould</c>'s removed <c>/api/users</c>
/// case: <c>ApplicationUser</c> is deliberately reachable at <c>/api/users</c> (ADR-0024), but only its
/// <c>[Attr]</c>-marked properties (<c>Email</c>, <c>DisplayName</c>) - this proves both the read shape and
/// the write lockdown (<c>GenerateControllerEndpoints = JsonApiEndpoints.Query</c> means no
/// Post/Patch/Delete controller action exists for this resource).
/// </remarks>
public class UsersResourceShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private ApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateUserClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ExposeOnlyEmailAndDisplayName_WhenFetchingAUser_ForGetSingle()
    {
        IdentityUserDto created = await CreateUserAsync();

        HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement attributes = document.RootElement.GetProperty("data").GetProperty("attributes");

        Assert.Equal(created.Email, attributes.GetProperty("email").GetString());
        Assert.Equal(created.DisplayName, attributes.GetProperty("displayName").GetString());

        Assert.False(attributes.TryGetProperty("passwordHash", out _));
        Assert.False(attributes.TryGetProperty("securityStamp", out _));
        Assert.False(attributes.TryGetProperty("concurrencyStamp", out _));
        Assert.False(attributes.TryGetProperty("lockoutEnd", out _));
        Assert.False(attributes.TryGetProperty("lockoutEnabled", out _));
        Assert.False(attributes.TryGetProperty("accessFailedCount", out _));
    }

    [Fact]
    public async Task SucceedWithACollection_WhenListingUsers_ForGetCollection()
    {
        await CreateUserAsync();

        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectTheRequest_WhenAttemptingToCreateAUser_ForPost()
    {
        var body = new
        {
            data = new
            {
                type = "users",
                attributes = new { email = "new@example.com", displayName = "New User" }
            }
        };

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/users", body);

        Assert.False(response.IsSuccessStatusCode);
    }

    private async Task<IdentityUserDto> CreateUserAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var dto = new IdentityUserDto
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = "Test User",
            PasswordHash = "initial-hash",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), dto);
        response.EnsureSuccessStatusCode();
        return dto;
    }

    /// <remarks>
    /// JSON:API content negotiation rejects a Content-Type with parameters (e.g. the charset
    /// <see cref="StringContent"/>'s 3-arg constructor would add) with 415, so the header is set explicitly
    /// below instead of going through that overload.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return await _client.SendAsync(request);
    }
}
