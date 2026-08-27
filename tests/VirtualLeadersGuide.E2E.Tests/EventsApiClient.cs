using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Creates, deletes, and lists Events directly against <c>Api</c>'s <c>/api/events</c> JSON:API resource -
/// for <see cref="AspireE2EFixture"/>'s fixture seeding (no <see cref="Microsoft.Playwright.IPage"/> exists
/// yet at that point, unlike every UI-driven Event creation elsewhere in this project) and for
/// <see cref="E2ETestBase"/>'s tracked per-test cleanup and this fixture's own run-end sweep (ADR-0039).
/// </summary>
/// <remarks>
/// Unlike <see cref="IdentityApiClient"/>, <c>/api/*</c> sits behind
/// <see cref="InternalJwtDefaults.PolicyName"/> (<c>Program.cs</c>), so every request here also carries a
/// bearer token minted with an Admin role claim (<see cref="InternalAdminJwt"/>, via
/// <see cref="AdminJsonApiClientBase"/>) - <c>EventAccessPolicy.CanDelete</c>/<c>CanCreate</c> are Admin-only
/// (ADR-0031).
/// </remarks>
public sealed class EventsApiClient(HttpClient httpClient, string internalJwtSigningKey)
    : AdminJsonApiClientBase(httpClient, internalJwtSigningKey)
{
    private const string EventsPath = "/api/events";
    private const string ResourceType = "events";

    /// <remarks>
    /// <c>DefaultIgnoreCondition = WhenWritingNull</c> is load-bearing, not cosmetic - mirrors
    /// <c>Web.Events.ApiEventClient</c>'s own <c>JsonOptions</c> for the same reason its remarks give: a POST
    /// body's unset <see cref="EventResourceObject.Id"/> must be omitted from the wire, not serialized as a
    /// literal <c>"id": null</c> - JsonApiDotNetCore's deserializer rejects that with a 422 ("Failed to
    /// convert ID '' of type 'Null' to type 'String'") rather than treating a missing key and an explicit
    /// null the same way. Distinct from the base class's own <see cref="AdminJsonApiClientBase.JsonOptions"/>
    /// (used for every read in this class) because that concern is specific to writing a POST body.
    /// </remarks>
    private static readonly JsonSerializerOptions CreateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates an Event with only a Name, directly via the JSON:API resource - no browser involved, for
    /// <see cref="AspireE2EFixture"/>'s own fixture seeding, which runs before any test's <c>Page</c> exists.
    /// Every other Event this project creates goes through the real UI instead (<see cref="E2ETestBase.CreateEventAsync"/>).
    /// </summary>
    /// <param name="name">The new Event's display name - Api derives the Slug and generates the Passcode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created Event's id.</returns>
    /// <exception cref="InvalidOperationException">The create request did not succeed.</exception>
    public async Task<Guid> CreateEventAsync(string name, CancellationToken cancellationToken)
    {
        var body = new EventDocument
        {
            Data = new EventResourceObject { Type = ResourceType, Attributes = new EventAttributesDto { Name = name } }
        };

        using HttpRequestMessage request = NewRequest(HttpMethod.Post, EventsPath);
        request.Content = JsonContent.Create(body, options: CreateJsonOptions);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is not HttpStatusCode.Created)
        {
            await ThrowForFailureAsync("create", name, response, cancellationToken);
        }

        EventDocument created = (await response.Content.ReadFromJsonAsync<EventDocument>(CreateJsonOptions, cancellationToken))!;
        return Guid.Parse(created.Data.Id!);
    }

    /// <summary>
    /// Every Event whose <c>Name</c> starts with <c>e2e-</c> (ADR-0039's discriminator) - a hand-made Event
    /// never matches, by construction.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The id and name of every matching Event, across all pages.</returns>
    /// <exception cref="InvalidOperationException">The list request did not succeed.</exception>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> ListE2EEventsAsync(CancellationToken cancellationToken)
    {
        string uri = BuildUnpagedCollectionUri(EventsPath, "startsWith(name,'e2e-')");
        using HttpRequestMessage request = NewRequest(HttpMethod.Get, uri);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("list", "e2e- Events", response, cancellationToken);
        }

        EventCollectionDocument document =
            (await response.Content.ReadFromJsonAsync<EventCollectionDocument>(JsonOptions, cancellationToken))!;
        return [.. document.Data.Select(resource => (Guid.Parse(resource.Id!), resource.Attributes!.Name!))];
    }

    /// <summary>Deletes the Event identified by <paramref name="id"/> - the cleanup half of Event creation (ADR-0039).</summary>
    /// <param name="id">The Event to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// Tolerates 404, unlike <see cref="AdminJsonApiClientBase.ThrowForFailureAsync"/>'s default use elsewhere
    /// in this class - a second delete attempt (a test's own tracked cleanup racing this class's run-end
    /// sweep, for example) must not fail teardown.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The delete request failed for a reason other than "already gone."</exception>
    public async Task DeleteEventAsync(Guid id, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = NewRequest(HttpMethod.Delete, $"{EventsPath}/{id}");

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode is not HttpStatusCode.NotFound)
        {
            await ThrowForFailureAsync("delete", id.ToString(), response, cancellationToken);
        }
    }

    /// <summary>Minimal JSON:API envelope shapes this class sends/reads - see <c>Web.Events.JsonApiDocument</c> for the fuller, production-facing version.</summary>
    private sealed class EventDocument
    {
        public required EventResourceObject Data { get; init; }
    }

    private sealed class EventCollectionDocument
    {
        public required List<EventResourceObject> Data { get; init; }
    }

    private sealed class EventResourceObject
    {
        public required string Type { get; init; }

        public string? Id { get; init; }

        public EventAttributesDto? Attributes { get; init; }
    }

    private sealed class EventAttributesDto
    {
        public string? Name { get; init; }
    }
}
