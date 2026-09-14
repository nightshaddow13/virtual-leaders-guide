using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Coverage for <c>/internal/public/*</c> (P4-2, #72): the anonymous-reachable lookup and passcode-check
/// surface behind the public Leaders Guide gate. Every test in this class deliberately uses
/// <see cref="ApiWebApplicationFactory.CreateAuthenticatedClient"/> (X-Internal-Key only, no internal JWT) -
/// unlike <c>EventsResourceShould</c>, which only ever uses <see cref="ApiWebApplicationFactory.CreateUserClient"/>
/// - proving this surface doesn't need a signed-in user's token the way <c>/api/*</c> does.
/// </remarks>
public class PublicGuideEndpointsShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Theory]
    [InlineData(EventStatus.Live)]
    [InlineData(EventStatus.Cancelled)]
    public async Task SucceedWithOk_WhenTheEventIsPubliclyVisible_ForGetBySlug(EventStatus status)
    {
        Event @event = await _factory.CreateEventAsync(status: status);
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{@event.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublicEventDto? dto = await response.Content.ReadFromJsonAsync<PublicEventDto>();
        Assert.NotNull(dto);
        Assert.Equal(@event.Id, dto.Id);
        Assert.Equal(status.ToString(), dto.Status);
        Assert.Equal(1, dto.PasscodeVersion);
    }

    [Fact]
    public async Task ReportPast_WhenALiveEventsEndsAtHasElapsed_ForGetBySlug()
    {
        Event @event = await _factory.CreateEventAsync(
            status: EventStatus.Live,
            startsAt: DateTimeOffset.UtcNow.AddDays(-10),
            endsAt: DateTimeOffset.UtcNow.AddDays(-1));
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{@event.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublicEventDto? dto = await response.Content.ReadFromJsonAsync<PublicEventDto>();
        Assert.Equal(nameof(EventStatus.Past), dto!.Status);
    }

    [Fact]
    public async Task NeverIncludeThePasscode_ForGetBySlug()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live, passcode: "TigerLantern");
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{@event.Slug}");

        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("TigerLantern", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"passcode\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenTheEventIsDraft_ForGetBySlug()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Draft);
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{@event.Slug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenNoEventHasThatSlug_ForGetBySlug()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/internal/public/events/no-such-event");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithMatched_WhenThePasscodeIsExactlyRight_ForPasscodeCheck()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live, passcode: "TigerLantern");

        PasscodeCheckResult result = await CheckAsync(@event.Slug!, "TigerLantern");

        Assert.True(result.Matched);
        Assert.Equal(@event.Id, result.EventId);
        Assert.Equal(1, result.PasscodeVersion);
    }

    [Theory]
    [InlineData("  TigerLantern  ")]
    [InlineData("tigerlantern")]
    [InlineData("TIGERLANTERN")]
    [InlineData("Tiger Lantern")]
    [InlineData("tiger lantern")]
    public async Task SucceedWithMatched_WhenThePasscodeOnlyDiffersByWhitespaceOrCase_ForPasscodeCheck(string guess)
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live, passcode: "TigerLantern");

        PasscodeCheckResult result = await CheckAsync(@event.Slug!, guess);

        Assert.True(result.Matched);
    }

    [Fact]
    public async Task SucceedWithNotMatched_WhenThePasscodeIsWrong_ForPasscodeCheck()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live, passcode: "TigerLantern");

        PasscodeCheckResult result = await CheckAsync(@event.Slug!, "WrongGuess");

        Assert.False(result.Matched);
        Assert.Null(result.EventId);
        Assert.Null(result.PasscodeVersion);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenTheEventIsDraft_ForPasscodeCheck()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Draft, passcode: "TigerLantern");
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/internal/public/events/{@event.Slug}/passcode", new PasscodeCheckRequest { Passcode = "TigerLantern" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenNoEventHasThatSlug_ForPasscodeCheck()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/internal/public/events/no-such-event/passcode", new PasscodeCheckRequest { Passcode = "TigerLantern" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<PasscodeCheckResult> CheckAsync(string slug, string guess)
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/internal/public/events/{slug}/passcode", new PasscodeCheckRequest { Passcode = guess });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PasscodeCheckResult>())!;
    }
}
