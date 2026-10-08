using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P5-11 (#96, add a Placement). Every Event and Activity here is created through the real UI within
/// the same scenario, and every Tab/Sub Tab/Section/Sub Section and Placement is created by "Place it" +
/// "Save changes" - none of them has a tracked teardown of its own: deleting the Event removes its whole
/// Tier+Placement subtree (<c>EventResourceDefinition.DeleteTierAndPlacementSubtreeAsync</c>), so
/// <see cref="E2ETestBase.TrackEvent"/> alone covers cleanup (ADR-0039's P5-11 rows). Tier names are
/// Guid-suffixed so a scenario can assert on its own text without colliding with another run's.
/// <c>RadzenAutoComplete</c> is typed into with a fill followed by Tab, so the value is committed by blur
/// rather than depending on the popup.
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class PlacementManagementScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>See <see cref="EventManagementScenarios.InteractiveTimeoutMs"/>'s identical remarks - every page here is <c>InteractiveServer</c> too.</remarks>
    private const int InteractiveTimeoutMs = 15_000;

    [Fact(DisplayName = "Given an Activity with no Placements, when an Admin places it under a brand-new Tab and saves, then the Tab appears lit in the tree and as a chip on the Activities list")]
    public async Task GivenAnActivityWithNoPlacements_WhenAnAdminPlacesItUnderANewTabAndSaves_ThenTheTabAppearsLitInTheTreeAndAsAChipOnTheList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Placement Host");
            (string activityName, Guid activityId) = await CreateActivityAsync(eventId, "Canoe Basics");
            string tab = $"e2e-Morning {Guid.NewGuid():n}";

            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await Expect(Page.GetByText("Not placed anywhere yet")).ToBeVisibleAsync(Interactive());
            await PlaceAsync(tab);

            await Expect(Page.Locator(".ptn-pending")).ToBeVisibleAsync(Interactive());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = InteractiveTimeoutMs });

            await Page.ReloadAsync();
            await Expect(Page.Locator(".ptn-lit")).ToHaveTextAsync(activityName, new LocatorAssertionsToHaveTextOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(ActivitiesListUrl(eventId));
            await Expect(Page.Locator(".al-chip")).ToHaveTextAsync(tab, new LocatorAssertionsToHaveTextOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an Activity, when an Admin places it at a Tab and Section with no Sub Tab, then the saved path shows both and skips the Sub Tab")]
    public async Task GivenAnActivity_WhenAnAdminPlacesItAtATabAndSectionWithNoSubTab_ThenTheSavedPathShowsBothAndSkipsTheSubTab() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Independent Chains Host");
            (_, Guid activityId) = await CreateActivityAsync(eventId, "Archery Range");
            string tab = $"e2e-Afternoon {Guid.NewGuid():n}";
            string section = $"e2e-Ranges {Guid.NewGuid():n}";

            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await Page.Locator("#PlacementTab").FillAsync(tab);
            await Page.Locator("#PlacementTab").PressAsync("Tab");
            await Page.Locator("#PlacementSection").FillAsync(section);
            await Page.Locator("#PlacementSection").PressAsync("Tab");
            await Expect(Page.Locator(".pb-path")).ToContainTextAsync(section, new LocatorAssertionsToContainTextOptions { Timeout = InteractiveTimeoutMs });
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            // Save is async - leaving before the ghost clears would navigate away mid-request.
            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(ActivitiesListUrl(eventId));
            await Expect(Page.Locator(".al-chip")).ToHaveTextAsync($"{tab} › {section}", new LocatorAssertionsToHaveTextOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an Activity already placed under a Tab, when an Admin tries to place it there again, then the builder warns of the duplicate and disables Place it")]
    public async Task GivenAnActivityAlreadyPlacedUnderATab_WhenAnAdminTriesToPlaceItThereAgain_ThenTheBuilderWarnsOfTheDuplicateAndDisablesPlaceIt() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Duplicate Host");
            (_, Guid activityId) = await CreateActivityAsync(eventId, "Swim Check");
            string tab = $"e2e-Morning {Guid.NewGuid():n}";

            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await PlaceAsync(tab);
            await Page.Locator("#PlacementTab").FillAsync(tab);
            await Page.Locator("#PlacementTab").PressAsync("Tab");

            await Expect(Page.GetByText("This activity is already placed here")).ToBeVisibleAsync(Interactive());
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" })).ToBeDisabledAsync();
        });

    [Fact(DisplayName = "Given an Event with an existing Tab, when an Admin focuses an empty Tab field, then the existing Tabs are listed before anything is typed")]
    public async Task GivenAnEventWithAnExistingTab_WhenAnAdminFocusesAnEmptyTabField_ThenTheExistingTabsAreListedBeforeAnythingIsTyped() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Options Host");
            (_, Guid first) = await CreateActivityAsync(eventId, "Canoe Basics");
            (_, Guid second) = await CreateActivityAsync(eventId, "Kayak Basics");
            string tab = $"e2e-Morning {Guid.NewGuid():n}";

            await Page.GotoAsync(ActivityUrl(eventId, first));
            await PlaceAsync(tab);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(ActivityUrl(eventId, second));
            await Page.Locator("#PlacementTab").ClickAsync();

            await Expect(Page.Locator(".rz-autocomplete-panel").GetByText(tab)).ToBeVisibleAsync(Interactive());
            await Expect(Page.Locator(".rz-autocomplete-panel")).ToContainTextAsync("1 activity", new LocatorAssertionsToContainTextOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an Activity with an unsaved ghost row, when an Admin clicks Cancel, then they are asked once and can keep editing or discard and leave")]
    public async Task GivenAnActivityWithAnUnsavedGhostRow_WhenAnAdminClicksCancel_ThenTheyAreAskedOnceAndCanKeepEditingOrDiscardAndLeave() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Leave Guard Host");
            (_, Guid activityId) = await CreateActivityAsync(eventId, "Leather Stamping");
            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await PlaceAsync($"e2e-Evening {Guid.NewGuid():n}");

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel" }).ClickAsync();
            await Expect(Page.GetByText("Discard unsaved placements?")).ToBeVisibleAsync(Interactive());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Keep editing" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(ActivityUrl(eventId, activityId));
            await Expect(Page.Locator(".ptn-pending")).ToBeVisibleAsync(Interactive());

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Discard and leave" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(ActivitiesListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
        });

    /// <remarks>Pins ADR-0069's posture extended to Placement: an assigned Director places exactly as an Admin does.</remarks>
    [Fact(DisplayName = "Given a Director assigned to an Event, when they place an Activity and save, then it succeeds the same way it would for an Admin")]
    public async Task GivenADirectorAssignedToAnEvent_WhenTheyPlaceAnActivityAndSave_ThenItSucceedsTheSameWayItWouldForAnAdmin() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Director Placement Host");
            (_, Guid activityId) = await CreateActivityAsync(eventId, "Nature Hike");
            await SignOutAsync();
            await CreateAndSignInDirectorAsync(eventId);
            string tab = $"e2e-Morning {Guid.NewGuid():n}";

            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await PlaceAsync(tab);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.Locator(".ptn-lit")).ToBeVisibleAsync(Interactive());
        });

    [Fact(DisplayName = "Given a Director not assigned to an Event, when navigating directly to one of its Activities, then they are denied")]
    public async Task GivenADirectorNotAssignedToAnEvent_WhenNavigatingDirectlyToOneOfItsActivities_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned For Placements");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned For Placements");
            (_, Guid activityId) = await CreateActivityAsync(unassignedEventId, "Off Limits");
            await SignOutAsync();
            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(ActivityUrl(unassignedEventId, activityId));

            await Expect(Page.GetByText("You don't have access to this Activity")).ToBeVisibleAsync(Interactive());
        });

    private async Task PlaceAsync(string tab)
    {
        await Page.Locator("#PlacementTab").FillAsync(tab);
        await Page.Locator("#PlacementTab").PressAsync("Tab");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
    }

    /// <remarks>Creates through the real new-Activity form, then reads the id back from the list row's navigation target by opening it.</remarks>
    private async Task<(string Name, Guid Id)> CreateActivityAsync(Guid eventId, string label)
    {
        string name = $"e2e-{label} {Guid.NewGuid():n}";
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/new").ToString());
        await Page.Locator("#Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(ActivitiesListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

        await Page.GetByText(name).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex($"/dashboard/events/{eventId}/activities/[0-9a-f-]{{36}}$"),
            new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
        Guid id = Guid.Parse(Page.Url[(Page.Url.LastIndexOf('/') + 1)..]);
        return (name, id);
    }

    private static LocatorAssertionsToBeVisibleOptions Interactive() => new() { Timeout = InteractiveTimeoutMs };

    private string ActivitiesListUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities").ToString();

    private string ActivityUrl(Guid eventId, Guid activityId) =>
        new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/{activityId}").ToString();

    /// <remarks>Kept local, not shared - see <see cref="InfoPageManagementScenarios"/>'s identical copy and its own header remarks.</remarks>
    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        IdentityUserDto director = await CreateTrackedUserAsync("e2e-director", CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, director.Email!, TestCredentials.KnownPassword);
        return director;
    }
}
