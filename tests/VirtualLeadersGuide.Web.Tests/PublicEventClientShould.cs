using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="ApiEventClientShould"/>'s discipline, but over the bare <c>"Api"</c> client
/// (<see cref="StubHttpClientFactory"/> directly, no <see cref="ApiClientTestFactory"/> chain) - matching
/// <see cref="PublicEventClient"/>'s own choice not to attach an internal JWT (P4-2, #72).
/// </remarks>
public class PublicEventClientShould
{
    [Fact]
    public async Task SendOnlyTheAcceptHeader_NoBearerToken_ForGetBySlugAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(EventDto()) };
        });
        PublicEventClient client = CreateClient(handler);

        await client.GetBySlugAsync("summer-camporee", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task ReturnSuccessWithTheEvent_WhenApiRespondsWithOk_ForGetBySlugAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EventDto(eventId));
        PublicEventClient client = CreateClient(handler);

        (PublicEventLookupOutcome outcome, PublicEventDto? @event) =
            await client.GetBySlugAsync("summer-camporee", CancellationToken.None);

        Assert.Equal(PublicEventLookupOutcome.Success, outcome);
        Assert.Equal(eventId, @event!.Id);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForGetBySlugAsync()
    {
        var handler = StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound);
        PublicEventClient client = CreateClient(handler);

        (PublicEventLookupOutcome outcome, PublicEventDto? @event) =
            await client.GetBySlugAsync("no-such-event", CancellationToken.None);

        Assert.Equal(PublicEventLookupOutcome.NotFound, outcome);
        Assert.Null(@event);
    }

    [Fact]
    public async Task ThrowPublicGuideUnavailable_WhenTheTransportFails_ForGetBySlugAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("boom"));
        PublicEventClient client = CreateClient(handler);

        await Assert.ThrowsAsync<PublicGuideUnavailableException>(
            () => client.GetBySlugAsync("summer-camporee", CancellationToken.None));
    }

    [Fact]
    public async Task ReturnMatched_WhenApiRespondsWithMatched_ForCheckPasscodeAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK,
            new PasscodeCheckResult { Matched = true, EventId = eventId, PasscodeVersion = 2 });
        PublicEventClient client = CreateClient(handler);

        (PasscodeCheckOutcome outcome, Guid? returnedEventId, int? version) =
            await client.CheckPasscodeAsync("summer-camporee", "TigerLantern", CancellationToken.None);

        Assert.Equal(PasscodeCheckOutcome.Matched, outcome);
        Assert.Equal(eventId, returnedEventId);
        Assert.Equal(2, version);
    }

    [Fact]
    public async Task ReturnNotMatched_WhenApiRespondsWithMatchedFalse_ForCheckPasscodeAsync()
    {
        var handler = StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK,
            new PasscodeCheckResult { Matched = false });
        PublicEventClient client = CreateClient(handler);

        (PasscodeCheckOutcome outcome, Guid? eventId, int? version) =
            await client.CheckPasscodeAsync("summer-camporee", "WrongGuess", CancellationToken.None);

        Assert.Equal(PasscodeCheckOutcome.NotMatched, outcome);
        Assert.Null(eventId);
        Assert.Null(version);
    }

    [Fact]
    public async Task ReturnEventNotFound_WhenApiRespondsWithNotFound_ForCheckPasscodeAsync()
    {
        var handler = StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound);
        PublicEventClient client = CreateClient(handler);

        (PasscodeCheckOutcome outcome, Guid? eventId, int? version) =
            await client.CheckPasscodeAsync("no-such-event", "TigerLantern", CancellationToken.None);

        Assert.Equal(PasscodeCheckOutcome.EventNotFound, outcome);
        Assert.Null(eventId);
        Assert.Null(version);
    }

    private static PublicEventClient CreateClient(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler));

    private static PublicEventDto EventDto(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Summer Camporee",
        Slug = "summer-camporee",
        Status = "Live",
        PasscodeVersion = 1
    };
}
