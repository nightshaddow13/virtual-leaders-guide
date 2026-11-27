using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Coverage for <c>/internal/public/*</c> (P4-2, #72; P5-14, #88): the anonymous-reachable lookup,
/// passcode-check, and guide surface behind the public Leaders Guide gate. Every test in this class
/// deliberately uses
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

    [Fact]
    public async Task OrderTiersBySortOrderThenName_WhenTheyAreSiblings_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab last = await _factory.CreateTabAsync(@event.Id, "Alpha", sortOrder: 1);
        Tab second = await _factory.CreateTabAsync(@event.Id, "morning", sortOrder: 0);
        Tab first = await _factory.CreateTabAsync(@event.Id, "Evening", sortOrder: 0);
        SubTab laterSubTab = await _factory.CreateSubTabAsync(@event.Id, first.Id, "Aardvark", sortOrder: 1);
        SubTab earlierSubTab = await _factory.CreateSubTabAsync(@event.Id, first.Id, "Zebra", sortOrder: 0);
        Section laterSection = await _factory.CreateSectionUnderTabAsync(@event.Id, first.Id, "Aquatics", sortOrder: 1);
        Section earlierSection = await _factory.CreateSectionUnderTabAsync(@event.Id, first.Id, "Ranges", sortOrder: 0);
        SubSection laterSubSection = await _factory.CreateSubSectionAsync(@event.Id, earlierSection.Id, "Archery", sortOrder: 1);
        SubSection earlierSubSection = await _factory.CreateSubSectionAsync(@event.Id, earlierSection.Id, "BB", sortOrder: 0);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, last.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, second.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, first.Id, laterSubTab.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, first.Id, earlierSubTab.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, first.Id, sectionId: laterSection.Id);
        await _factory.CreateActivityPlacementAsync(
            @event.Id, activity.Id, first.Id, sectionId: earlierSection.Id, subSectionId: laterSubSection.Id);
        await _factory.CreateActivityPlacementAsync(
            @event.Id, activity.Id, first.Id, sectionId: earlierSection.Id, subSectionId: earlierSubSection.Id);

        PublicGuideDto guide = await GetGuideAsync(@event.Slug!);

        Assert.Equal(["Evening", "morning", "Alpha"], guide.Tabs.Select(t => t.Name));
        Assert.Equal(["Zebra", "Aardvark"], guide.Tabs[0].SubTabs.Select(s => s.Name));
        Assert.Equal(["Ranges", "Aquatics"], guide.Tabs[0].Screen.Sections.Select(s => s.Name));
        Assert.Equal(["BB", "Archery"], guide.Tabs[0].Screen.Sections[0].SubSections.Select(s => s.Name));
    }

    [Fact]
    public async Task OrderActivitiesByPlacementSortOrderThenName_WhenTheyShareAPath_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        Activity canoe = await _factory.CreateActivityAsync(@event.Id, "Canoe");
        Activity swim = await _factory.CreateActivityAsync(@event.Id, "Swim");
        Activity archery = await _factory.CreateActivityAsync(@event.Id, "archery");
        await _factory.CreateActivityPlacementAsync(@event.Id, canoe.Id, tab.Id, sortOrder: 1);
        await _factory.CreateActivityPlacementAsync(@event.Id, swim.Id, tab.Id, sortOrder: 0);
        await _factory.CreateActivityPlacementAsync(@event.Id, archery.Id, tab.Id, sortOrder: 1);

        PublicGuideDto guide = await GetGuideAsync(@event.Slug!);

        Assert.Equal(["Swim", "archery", "Canoe"], Assert.Single(guide.Tabs).Screen.Activities.Select(a => a.Name));
    }

    [Fact]
    public async Task KeepEachScreensContentToItself_WhenATabHasASubTab_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Tab tab = await _factory.CreateTabAsync(@event.Id, "Day");
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id, "Round Robin");
        Section bareSection = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id, "Canoeing");
        Section subTabSection = await _factory.CreateSectionUnderSubTabAsync(@event.Id, subTab.Id, "Canoeing");
        Activity loose = await _factory.CreateActivityAsync(@event.Id, "Loose");
        Activity paddle = await _factory.CreateActivityAsync(@event.Id, "Paddle");
        Activity rotation = await _factory.CreateActivityAsync(@event.Id, "Rotation");
        await _factory.CreateActivityPlacementAsync(@event.Id, loose.Id, tab.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, paddle.Id, tab.Id, sectionId: bareSection.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, loose.Id, tab.Id, subTab.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, rotation.Id, tab.Id, subTab.Id, subTabSection.Id);

        PublicGuideTabDto day = Assert.Single((await GetGuideAsync(@event.Slug!)).Tabs);

        Assert.Equal("Loose", Assert.Single(day.Screen.Activities).Name);
        Assert.Equal("Paddle", Assert.Single(Assert.Single(day.Screen.Sections).Activities).Name);
        PublicGuideScreenDto roundRobin = Assert.Single(day.SubTabs).Screen;
        Assert.Equal("Loose", Assert.Single(roundRobin.Activities).Name);
        Assert.Equal("Rotation", Assert.Single(Assert.Single(roundRobin.Sections).Activities).Name);
    }

    [Fact]
    public async Task ReturnTheInfoPageAloneOnItsScreen_WhenOneIsPlacedThere_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Tab tab = await _factory.CreateTabAsync(@event.Id, "Logistics");
        InfoPage page = await _factory.CreateInfoPageAsync(@event.Id, "About", "Welcome to **camp**!");
        await _factory.CreateInfoPagePlacementAsync(@event.Id, page.Id, tab.Id);

        PublicGuideScreenDto screen = Assert.Single((await GetGuideAsync(@event.Slug!)).Tabs).Screen;

        Assert.Equal("About", screen.InfoPage!.Title);
        Assert.Equal("Welcome to **camp**!", screen.InfoPage.MarkdownContent);
        Assert.Empty(screen.Activities);
        Assert.Empty(screen.Sections);
    }

    [Fact]
    public async Task ReturnOnlyThisEventsPlacedContent_WhenOtherContentExists_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Event other = await _factory.CreateEventAsync(status: EventStatus.Live);
        Tab tab = await _factory.CreateTabAsync(@event.Id, "Day");
        Activity placed = await _factory.CreateActivityAsync(@event.Id, "Placed activity");
        await _factory.CreateActivityPlacementAsync(@event.Id, placed.Id, tab.Id);
        await _factory.CreateActivityAsync(@event.Id, "Unplaced activity");
        await _factory.CreateInfoPageAsync(@event.Id, "Unplaced page");
        Tab otherTab = await _factory.CreateTabAsync(other.Id, "Other");
        Activity otherActivity = await _factory.CreateActivityAsync(other.Id);
        await _factory.CreateActivityPlacementAsync(other.Id, otherActivity.Id, otherTab.Id);

        PublicGuideTabDto only = Assert.Single((await GetGuideAsync(@event.Slug!)).Tabs);

        Assert.Equal("Day", only.Name);
        Assert.Null(only.Screen.InfoPage);
        Assert.Equal("Placed activity", Assert.Single(only.Screen.Activities).Name);
        Assert.Empty(only.Screen.Sections);
        Assert.Empty(only.SubTabs);
    }

    [Fact]
    public async Task LeaveOutSectionsAndSubSections_WhenTheyHaveNoActivities_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id, "Empty");
        Section used = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id, "Used");
        await _factory.CreateSubSectionAsync(@event.Id, used.Id, "Empty too");
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id, sectionId: used.Id);

        PublicGuideScreenDto screen = Assert.Single((await GetGuideAsync(@event.Slug!)).Tabs).Screen;

        PublicGuideSectionDto section = Assert.Single(screen.Sections);
        Assert.Equal("Used", section.Name);
        Assert.Empty(section.SubSections);
    }

    [Fact]
    public async Task MergeSiblingTiers_WhenTheyShareAName_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Live);
        Tab first = await _factory.CreateTabAsync(@event.Id, "Day");
        Tab second = await _factory.CreateTabAsync(@event.Id, "day", sortOrder: 1);
        SubTab firstSubTab = await _factory.CreateSubTabAsync(@event.Id, first.Id, "Open");
        SubTab secondSubTab = await _factory.CreateSubTabAsync(@event.Id, second.Id, "Open", sortOrder: 1);
        Section firstSection = await _factory.CreateSectionUnderTabAsync(@event.Id, first.Id, "Ranges");
        Section secondSection = await _factory.CreateSectionUnderTabAsync(@event.Id, second.Id, "Ranges", sortOrder: 1);
        SubSection firstSubSection = await _factory.CreateSubSectionAsync(@event.Id, firstSection.Id, "BB");
        SubSection secondSubSection = await _factory.CreateSubSectionAsync(@event.Id, secondSection.Id, "BB", sortOrder: 1);
        Activity canoe = await _factory.CreateActivityAsync(@event.Id, "Canoe");
        Activity swim = await _factory.CreateActivityAsync(@event.Id, "Swim");
        await _factory.CreateActivityPlacementAsync(@event.Id, swim.Id, first.Id, sortOrder: 1);
        await _factory.CreateActivityPlacementAsync(@event.Id, canoe.Id, second.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, canoe.Id, first.Id, firstSubTab.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, swim.Id, second.Id, secondSubTab.Id, sortOrder: 1);
        await _factory.CreateActivityPlacementAsync(
            @event.Id, canoe.Id, first.Id, sectionId: firstSection.Id, subSectionId: firstSubSection.Id);
        await _factory.CreateActivityPlacementAsync(
            @event.Id, swim.Id, second.Id, sectionId: secondSection.Id, subSectionId: secondSubSection.Id, sortOrder: 1);

        PublicGuideTabDto day = Assert.Single((await GetGuideAsync(@event.Slug!)).Tabs);

        Assert.Equal("Day", day.Name);
        Assert.Equal(["Canoe", "Swim"], day.Screen.Activities.Select(a => a.Name));
        Assert.Equal(["Canoe", "Swim"], Assert.Single(day.SubTabs).Screen.Activities.Select(a => a.Name));
        PublicGuideSubSectionDto bb = Assert.Single(Assert.Single(day.Screen.Sections).SubSections);
        Assert.Equal(["Canoe", "Swim"], bb.Activities.Select(a => a.Name));
    }

    [Fact]
    public async Task Succeed_WhenTheEventIsCancelled_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Cancelled);
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{@event.Slug}/guide");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenTheEventIsDraft_ForGetGuide()
    {
        Event @event = await _factory.CreateEventAsync(status: EventStatus.Draft);
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{@event.Slug}/guide");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenNoEventHasThatSlug_ForGetGuide()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/internal/public/events/no-such-event/guide");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<PublicGuideDto> GetGuideAsync(string slug)
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.GetAsync($"/internal/public/events/{slug}/guide");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PublicGuideDto>())!;
    }
}
