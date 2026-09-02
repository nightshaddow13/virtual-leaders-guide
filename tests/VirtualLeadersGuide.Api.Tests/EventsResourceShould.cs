using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive coverage for <c>/api/events</c> (P2-7, #16): <c>EventResourceDefinition</c>'s Admin/Director
/// scoping. Doesn't repeat <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c>'s negative case for
/// <c>roles</c>/<c>userRoles</c>, or <c>InternalJwtAuthorizationShould</c>'s identity-forwarding policy tests
/// (this class only ever calls <see cref="ApiWebApplicationFactory.CreateUserClient"/>, never an
/// X-Internal-Key-only client). Grants are simulated entirely via pre-formatted role claims
/// (<see cref="ApiWebApplicationFactory.AdminRoleClaim"/>/<see cref="ApiWebApplicationFactory.DirectorRoleClaim"/>)
/// rather than real <c>UserRoles</c> rows - Api authorizes from JWT claims alone (ADR-0007's amendment).
/// </remarks>
public class EventsResourceShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SucceedWithCreated_WhenAdminCreatesAnEventWithOnlyAName_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "events", attributes = new { name = $"Fall Camporee {Guid.NewGuid()}" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(attributes.GetProperty("slug").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(attributes.GetProperty("passcode").GetString()));
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAnyEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminUpdatesAnyEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { name = $"Renamed {Guid.NewGuid()}" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesAnyEvent_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenAnAssignedDirectorReadsTheirEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorUpdatesTheirEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { name = $"Renamed {Guid.NewGuid()}" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorAttemptsToCreateAnEvent_ForPost()
    {
        Event existing = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(existing.Id);
        var body = new { data = new { type = "events", attributes = new { name = $"New Event {Guid.NewGuid()}" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorAttemptsToDeleteTheirEvent_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorReadsAnotherEvent_ForGetSingle()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{other.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorUpdatesAnotherEvent_ForPatch()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { name = $"Renamed {Guid.NewGuid()}" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorDeletesAnotherEvent_ForDelete()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{other.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminDeletesANonexistentEvent_ForDelete()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <remarks>
    /// HTTP-level counterpart to <c>EventSchemaShould.CascadeDeleteEventScopedGrants_WhenTheEventIsDeleted_ForSaveChanges</c>,
    /// which only proves the cascade at the DbContext level - this proves it fires through the actual
    /// JsonApiDotNetCore delete pipeline too (P2-17, #112; ADR-0044's Question 14).
    /// </remarks>
    [Fact]
    public async Task CascadeDeleteTheDirectorsGrant_WhenAdminDeletesAnEventWithAnAssignedDirector_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        ApplicationUser director = await _factory.CreateUserAsync();
        UserRole grant = await _factory.CreateGrantAsync(director.Id, RoleIds.Director, @event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Assert.False(await dbContext.DomainUserRoles.AnyAsync(g => g.Id == grant.Id));
    }

    [Fact]
    public async Task ReturnOnlyTheAssignedEvent_WhenADirectorListsEvents_ForGetCollection()
    {
        Event assigned = await _factory.CreateEventAsync();
        await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(assigned.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement[] data = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(data);
        Assert.Equal(assigned.Id.ToString(), data[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ReturnAnEmptyCollection_WhenACallerWithNoRoleClaimsListsEvents_ForGetCollection()
    {
        await _factory.CreateEventAsync();
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsReadsAnEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithConflict_WhenCreatingAnEventWhoseNameDerivesToAnAlreadyUsedSlug_ForPost()
    {
        Event existing = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "events", attributes = new { name = existing.Name.ToUpperInvariant() } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name", "/data/attributes/slug");
    }

    [Fact]
    public async Task RejectWithConflict_WhenUpdatingAnEventsNameToOneAlreadyInUse_ForPatch()
    {
        Event existing = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { name = existing.Name.ToUpperInvariant() }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name");
    }

    [Fact]
    public async Task RejectWithConflict_WhenUpdatingAnEventsSlugToOneAlreadyInUse_ForPatch()
    {
        Event existing = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { slug = existing.Slug }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/slug");
    }

    [Fact]
    public async Task RejectWithConflict_WhenUpdatingAnEventToANameAndSlugBothAlreadyInUse_ForPatch()
    {
        Event existing = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { name = existing.Name, slug = existing.Slug }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name", "/data/attributes/slug");
    }

    [Fact]
    public async Task SucceedWithCreated_WhenAdminCreatesAnEventWithBothDates_ForPost()
    {
        using HttpClient client = AdminClient();
        var startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.FromHours(-5));
        var endsAt = new DateTimeOffset(2026, 6, 14, 17, 0, 0, TimeSpan.FromHours(-5));
        var body = new
        {
            data = new
            {
                type = "events",
                attributes = new { name = $"Fall Camporee {Guid.NewGuid()}", startsAt, endsAt }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal(startsAt.ToUniversalTime(), attributes.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(endsAt.ToUniversalTime(), attributes.GetProperty("endsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminSetsAnEndOnAnEventThatAlreadyHasAStart_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(startsAt: DateTimeOffset.UtcNow);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = DateTimeOffset.UtcNow.AddDays(1) }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenEndEqualsStart_ForPatch()
    {
        var startsAt = DateTimeOffset.UtcNow;
        Event @event = await _factory.CreateEventAsync(startsAt: startsAt);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = startsAt }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/endsAt");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenEndPrecedesStart_ForPatch()
    {
        var startsAt = DateTimeOffset.UtcNow;
        Event @event = await _factory.CreateEventAsync(startsAt: startsAt);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = startsAt.AddDays(-1) }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/endsAt");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAnEndIsSetOnAnEventWithNoStart_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = DateTimeOffset.UtcNow }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/startsAt");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenClearingStartWhileEndRemainsSet_ForPatch()
    {
        var startsAt = DateTimeOffset.UtcNow;
        Event @event = await _factory.CreateEventAsync(startsAt: startsAt);
        using HttpClient adminClient = AdminClient();
        await SendAsync(adminClient, HttpMethod.Patch, $"/api/events/{@event.Id}",
            new { data = new { type = "events", id = @event.Id.ToString(), attributes = new { endsAt = startsAt.AddDays(1) } } });

        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { startsAt = (DateTimeOffset?)null }
            }
        };
        HttpResponseMessage response = await SendAsync(adminClient, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/startsAt");
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenClearingBothDates_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(startsAt: DateTimeOffset.UtcNow);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { startsAt = (DateTimeOffset?)null, endsAt = (DateTimeOffset?)null }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        HttpResponseMessage getResponse = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");
        JsonElement attributes = await AttributesOfAsync(getResponse);
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("startsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("endsAt").ValueKind);
    }

    [Fact]
    public async Task PersistAsUtc_WhenCreatingAnEventWithANonUtcOffset_ForPost()
    {
        using HttpClient client = AdminClient();
        var startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.FromHours(9));
        var body = new
        {
            data = new { type = "events", attributes = new { name = $"Fall Camporee {Guid.NewGuid()}", startsAt } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        JsonElement attributes = await AttributesOfAsync(response);
        DateTimeOffset returned = attributes.GetProperty("startsAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, returned.Offset);
        Assert.Equal(startsAt.ToUniversalTime(), returned);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorSetsDates_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { startsAt = DateTimeOffset.UtcNow }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnDraft_WhenAdminCreatesAnEventWithNoExplicitStatus_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "events", attributes = new { name = $"Fall Camporee {Guid.NewGuid()}" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal("Draft", attributes.GetProperty("status").GetString());
    }

    /// <remarks>
    /// <see cref="Event.Status"/> carries no <see cref="AttrCapabilities.AllowCreate"/> - this asserts
    /// JsonApiDotNetCore's own built-in rejection of a no-AllowCreate attribute in a POST body, not code this
    /// story wrote (<see cref="EventResourceDefinition.OnWritingAsync"/>'s remarks).
    /// </remarks>
    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAPostBodySetsStatus_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events",
                attributes = new { name = $"Fall Camporee {Guid.NewGuid()}", status = "Live" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/status");
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminMarksADraftEventLive_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        { data = new { type = "events", id = @event.Id.ToString(), attributes = new { status = "Live" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        HttpResponseMessage getResponse = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");
        Assert.Equal("Live", (await AttributesOfAsync(getResponse)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminCancelsALiveEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        using HttpClient client = AdminClient();
        var body = new
        { data = new { type = "events", id = @event.Id.ToString(), attributes = new { status = "Cancelled" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        HttpResponseMessage getResponse = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");
        Assert.Equal("Cancelled", (await AttributesOfAsync(getResponse)).GetProperty("status").GetString());
    }

    /// <remarks>
    /// A same-status re-PATCH (here, <c>Live</c> to <c>Live</c>) is a deliberate no-op, not a 422 - a
    /// defensive allowance for a retried request; see <see cref="EventResourceDefinition.ValidateStatusTransitionAsync"/>'s
    /// remarks. The Web client never constructs one deliberately.
    /// </remarks>
    [Fact]
    public async Task SucceedWithNoContent_WhenAdminRePatchesTheSameStatus_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        using HttpClient client = AdminClient();
        var body = new
        { data = new { type = "events", id = @event.Id.ToString(), attributes = new { status = "Live" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(EventStatus.Draft, "Cancelled")]
    [InlineData(EventStatus.Live, "Draft")]
    [InlineData(EventStatus.Draft, "Past")]
    [InlineData(EventStatus.Live, "Past")]
    [InlineData(EventStatus.Cancelled, "Live")]
    [InlineData(EventStatus.Cancelled, "Draft")]
    public async Task RejectWithUnprocessableEntity_WhenAnIllegalStatusTransitionIsAttempted_ForPatch(
        EventStatus from, string to)
    {
        Event @event = await _factory.CreateEventAsync(status: from);
        using HttpClient client = AdminClient();
        var body = new
        { data = new { type = "events", id = @event.Id.ToString(), attributes = new { status = to } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/status");
    }

    /// <remarks>
    /// A <c>Live</c> Event whose <see cref="Event.EndsAt"/> has already elapsed is effectively
    /// <see cref="EventStatus.Past"/> (<see cref="EventResourceDefinition.OnSerialize"/>) - cancelling it is
    /// exactly as illegal as cancelling a Past Event that was already Past when it went Live (ADR-0044:
    /// "a Past Event can't be cancelled retroactively"). This is a single-resource PATCH, so it never touches
    /// <see cref="EventResourceDefinition.OnApplyFilter"/>'s SQLite-limited collection filtering (see
    /// <c>EventStatusFilterRewriter</c>'s remarks) - the elapsed check here runs in
    /// <see cref="EventResourceDefinition.ValidateStatusTransitionAsync"/>, entirely in memory after
    /// materialization, so it's fully correct under SQLite too.
    /// </remarks>
    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenCancellingAnAlreadyElapsedLiveEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(
            startsAt: DateTimeOffset.UtcNow.AddDays(-2), endsAt: DateTimeOffset.UtcNow.AddDays(-1),
            status: EventStatus.Live);
        using HttpClient client = AdminClient();
        var body = new
        { data = new { type = "events", id = @event.Id.ToString(), attributes = new { status = "Cancelled" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/status");
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorChangesStatus_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        { data = new { type = "events", id = @event.Id.ToString(), attributes = new { status = "Live" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <remarks>
    /// <see cref="EventResourceDefinition.OnSerialize"/> computes <see cref="EventStatus.Past"/> in memory
    /// after materialization - a single-resource <c>GET</c> never goes through
    /// <see cref="EventResourceDefinition.OnApplyFilter"/>'s collection-level filtering at all (single-resource
    /// requests return their existing filter unchanged), so this is fully correct under SQLite despite the
    /// provider's inability to translate a <see cref="DateTimeOffset"/> comparison to SQL - see
    /// <c>EventStatusFilterRewriter</c>'s remarks for that separate, collection-only limitation.
    /// </remarks>
    [Fact]
    public async Task ReturnPast_WhenALiveEventsEndHasElapsed_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync(
            startsAt: DateTimeOffset.UtcNow.AddDays(-2), endsAt: DateTimeOffset.UtcNow.AddDays(-1),
            status: EventStatus.Live);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal("Past", (await AttributesOfAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReturnDraft_WhenADraftEventsEndHasElapsed_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync(
            startsAt: DateTimeOffset.UtcNow.AddDays(-2), endsAt: DateTimeOffset.UtcNow.AddDays(-1));
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal("Draft", (await AttributesOfAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReturnLive_WhenALiveEventHasNoEnd_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal("Live", (await AttributesOfAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ExcludeCancelledEvents_WhenNoStatusFilterIsSupplied_ForGetCollection()
    {
        Event visible = await _factory.CreateEventAsync();
        Event cancelled = await _factory.CreateEventAsync(status: EventStatus.Cancelled);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/events");

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] ids = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToArray();
        Assert.Contains(visible.Id.ToString(), ids);
        Assert.DoesNotContain(cancelled.Id.ToString(), ids);
    }

    [Fact]
    public async Task IncludeCancelledEvents_WhenFilteringOnCancelled_ForGetCollection()
    {
        Event cancelled = await _factory.CreateEventAsync(status: EventStatus.Cancelled);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Get, "/api/events?filter=equals(status,'Cancelled')");

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] ids = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToArray();
        Assert.Contains(cancelled.Id.ToString(), ids);
    }

    [Fact]
    public async Task ExcludeCancelledEvents_WhenFilteringOnDraft_ForGetCollection()
    {
        Event draft = await _factory.CreateEventAsync();
        Event cancelled = await _factory.CreateEventAsync(status: EventStatus.Cancelled);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Get, "/api/events?filter=equals(status,'Draft')");

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] ids = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToArray();
        Assert.Contains(draft.Id.ToString(), ids);
        Assert.DoesNotContain(cancelled.Id.ToString(), ids);
    }

    [Fact]
    public async Task IncludeAnUndatedLiveEvent_WhenFilteringOnLive_ForGetCollection()
    {
        Event live = await _factory.CreateEventAsync(status: EventStatus.Live);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Get, "/api/events?filter=equals(status,'Live')");

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] ids = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToArray();
        Assert.Contains(live.Id.ToString(), ids);
    }

    /// <remarks>
    /// A single-resource read (<see cref="EventResourceDefinition.OnApplyFilter"/> never status-narrows one) -
    /// exactly the AC's "reachable by direct URL" - proven for <see cref="EventStatus.Cancelled"/> here since
    /// <see cref="ReturnPast_WhenALiveEventsEndHasElapsed_ForGetSingle"/> already covers the computed-Past case.
    /// </remarks>
    [Fact]
    public async Task SucceedWithOk_WhenReadingACancelledEventDirectly_ForGetSingle()
    {
        Event cancelled = await _factory.CreateEventAsync(status: EventStatus.Cancelled);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{cancelled.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <remarks>
    /// The AC this exists for: <see cref="Event.Name"/> stops being globally unique once a database index no
    /// longer backs it (ADR-0053) - a <see cref="EventStatus.Cancelled"/> Event's Name is free to reuse. This
    /// is <see cref="EventResourceDefinition.CheckForConflictsAsync"/>'s effective-status check operating
    /// entirely on materialized values, so it's unaffected by the SQLite <see cref="DateTimeOffset"/>
    /// limitation elsewhere in this story.
    /// </remarks>
    /// <remarks>
    /// Uses an explicit, distinct Slug on the Cancelled Event - two Events sharing a Name otherwise derive
    /// the identical default Slug (<see cref="ApiWebApplicationFactory.CreateEventAsync"/>'s remarks), and
    /// <see cref="Event.Slug"/> stays permanently, unconditionally unique regardless of Status. Without this,
    /// the new Event's own auto-derived Slug would collide with the Cancelled Event's Slug and this would 409
    /// for an unrelated reason, masking whether the Name-reuse rule under test actually works.
    /// </remarks>
    [Fact]
    public async Task SucceedWithCreated_WhenReusingACancelledEventsName_ForPost()
    {
        Event cancelled = await _factory.CreateEventAsync(
            status: EventStatus.Cancelled, slug: $"cancelled-{Guid.NewGuid():n}");
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "events", attributes = new { name = cancelled.Name } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <remarks>See <see cref="SucceedWithCreated_WhenReusingACancelledEventsName_ForPost"/>'s remarks for why an explicit Slug is needed here too.</remarks>
    [Fact]
    public async Task SucceedWithCreated_WhenReusingAnEffectivelyPastEventsName_ForPost()
    {
        Event elapsed = await _factory.CreateEventAsync(
            startsAt: DateTimeOffset.UtcNow.AddDays(-2), endsAt: DateTimeOffset.UtcNow.AddDays(-1),
            status: EventStatus.Live, slug: $"elapsed-{Guid.NewGuid():n}");
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "events", attributes = new { name = elapsed.Name } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesACancelledEvent_ForDelete()
    {
        Event cancelled = await _factory.CreateEventAsync(status: EventStatus.Cancelled);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{cancelled.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private HttpClient AdminClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.AdminRoleClaim()]);

    private HttpClient DirectorClient(Guid eventId) =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.DirectorRoleClaim(eventId)]);

    private static async Task AssertErrorPointersAsync(HttpResponseMessage response, params string[] expectedPointers)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] actualPointers = document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("source").GetProperty("pointer").GetString()!)
            .ToArray();
        Assert.Equal(expectedPointers.Order(), actualPointers.Order());
    }

    private static async Task<JsonElement> AttributesOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("attributes").Clone();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string requestUri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return await client.SendAsync(request);
    }
}
