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
public class PlacementManagementScenarios : E2ETestBase
{
    [Fact(DisplayName = "Given an Activity with no Placements, when an Admin places it under a brand-new Tab and saves, then the Tab appears lit in the tree and as a chip on the Activities list")]
    public async Task GivenAnActivityWithNoPlacements_WhenAnAdminPlacesItUnderANewTabAndSaves_ThenTheTabAppearsLitInTheTreeAndAsAChipOnTheList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Placement Host");
            (string activityName, Guid activityId) = await CreateActivityAsync(eventId, "Canoe Basics");
            string tab = $"e2e-Morning {Guid.NewGuid():n}";

            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await Expect(Page.GetByText("Not placed anywhere yet")).ToBeVisibleAsync(InteractiveVisible());
            await PlaceAsync(tab);

            await Expect(Page.Locator(".ptn-pending")).ToBeVisibleAsync(InteractiveVisible());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());

            await Page.ReloadAsync();
            await Expect(Page.Locator(".ptn-lit")).ToHaveTextAsync(activityName, InteractiveText());

            await Page.GotoAsync(ActivitiesListUrl(eventId));
            await Expect(Page.Locator(".al-chip")).ToHaveTextAsync(tab, InteractiveText());
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
            await Expect(Page.Locator(".pb-path")).ToContainTextAsync(section, InteractiveContainText());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            // Save is async - leaving before the ghost clears would navigate away mid-request.
            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());

            await Page.GotoAsync(ActivitiesListUrl(eventId));
            await Expect(Page.Locator(".al-chip")).ToHaveTextAsync($"{tab} › {section}", InteractiveText());
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

            await Expect(Page.GetByText("This activity is already placed here")).ToBeVisibleAsync(InteractiveVisible());
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
            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());

            await Page.GotoAsync(ActivityUrl(eventId, second));
            await Page.Locator("#PlacementTab").ClickAsync();

            await Expect(Page.Locator(".rz-autocomplete-panel").GetByText(tab)).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.Locator(".rz-autocomplete-panel")).ToContainTextAsync("1 activity", InteractiveContainText());
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
            await Expect(Page.GetByText("Discard unsaved placements?")).ToBeVisibleAsync(InteractiveVisible());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Keep editing" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(ActivityUrl(eventId, activityId));
            await Expect(Page.Locator(".ptn-pending")).ToBeVisibleAsync(InteractiveVisible());

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Discard and leave" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(ActivitiesListUrl(eventId), InteractiveUrl());
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

            await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());
            await Expect(Page.Locator(".ptn-lit")).ToBeVisibleAsync(InteractiveVisible());
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

            await Expect(Page.GetByText("You don't have access to this Activity")).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given an Activity placed under two Tabs that another Activity shares, when an Admin removes one, then it goes with no dialog and Undo restores it")]
    public async Task GivenAnActivityPlacedUnderTwoTabs_WhenAnAdminRemovesOneWhoseTabAnotherActivityShares_ThenItGoesWithNoDialogAndUndoRestoresIt() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Remove Host");
            (_, Guid canoe) = await CreateActivityAsync(eventId, "Canoe Basics");
            (_, Guid swim) = await CreateActivityAsync(eventId, "Swim Check");
            string morning = $"e2e-Morning {Guid.NewGuid():n}";
            string afternoon = $"e2e-Afternoon {Guid.NewGuid():n}";
            await PlaceAndSaveAsync(eventId, swim, morning, afternoon);
            await PlaceAndSaveAsync(eventId, canoe, morning, afternoon);

            await Page.GotoAsync(ActivityUrl(eventId, canoe));
            await Expect(Page.Locator(".ptn-lit")).ToHaveCountAsync(2, InteractiveCount());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Remove placement {morning}" }).ClickAsync();

            await Expect(Page.Locator(".ptn-lit")).ToHaveCountAsync(1, InteractiveCount());
            await Expect(Page.GetByText($"Removed from {morning}")).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.Locator(".rz-dialog-title")).ToHaveCountAsync(0);

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Undo" }).ClickAsync();
            await Expect(Page.Locator(".ptn-lit")).ToHaveCountAsync(2, InteractiveCount());
        });

    /// <remarks>The one scenario that proves the delete ordering on SQL Server (the Api suite runs on SQLite): the Placement and the Tab it alone kept alive go in a single save.</remarks>
    [Fact(DisplayName = "Given the only Activity in a Tab, when an Admin removes that Placement and confirms, then the Tab leaves the tree and the autofill")]
    public async Task GivenTheOnlyActivityInATab_WhenAnAdminRemovesThatPlacementAndConfirms_ThenTheTabLeavesTheTreeAndTheAutofill() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Reap Host");
            (_, Guid canoe) = await CreateActivityAsync(eventId, "Canoe Basics");
            string kept = $"e2e-Afternoon {Guid.NewGuid():n}";
            string reaped = $"e2e-Morning {Guid.NewGuid():n}";
            await PlaceAndSaveAsync(eventId, canoe, kept, reaped);

            await Page.GotoAsync(ActivityUrl(eventId, canoe));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Remove placement {reaped}" }).ClickAsync();
            await Expect(Page.Locator(".rz-dialog-title")).ToHaveTextAsync("Remove this placement?", InteractiveText());
            await Expect(Page.GetByText($"This is the only activity in {reaped}")).ToBeVisibleAsync(InteractiveVisible());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove", Exact = true }).ClickAsync();

            await Expect(Page.Locator(".ptn-lit")).ToHaveCountAsync(1, InteractiveCount());
            await Page.ReloadAsync();
            await Page.Locator("#PlacementTab").ClickAsync();
            await Expect(Page.Locator(".rz-autocomplete-panel").GetByText(kept)).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.Locator(".rz-autocomplete-panel").GetByText(reaped)).ToHaveCountAsync(0);
        });

    [Fact(DisplayName = "Given an Activity with one Placement, when an Admin tries to remove it and keeps the Activity, then the Placement stays")]
    public async Task GivenAnActivityWithOnePlacement_WhenAnAdminTriesToRemoveItAndKeepsTheActivity_ThenThePlacementStays() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Last Placement Host");
            (_, Guid canoe) = await CreateActivityAsync(eventId, "Canoe Basics");
            string tab = $"e2e-Morning {Guid.NewGuid():n}";
            await PlaceAndSaveAsync(eventId, canoe, tab);

            await Page.GotoAsync(ActivityUrl(eventId, canoe));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Remove placement {tab}" }).ClickAsync();

            await Expect(Page.GetByText("An activity needs at least one placement.")).ToBeVisibleAsync(InteractiveVisible());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Keep it" }).ClickAsync();
            await Page.ReloadAsync();
            await Expect(Page.Locator(".ptn-lit")).ToHaveCountAsync(1, InteractiveCount());
        });

    [Fact(DisplayName = "Given an Activity with one Placement, when an Admin removes it and chooses Delete activity, then they land on the list without that Activity")]
    public async Task GivenAnActivityWithOnePlacement_WhenAnAdminRemovesItAndChoosesDeleteActivity_ThenTheyLandOnTheListWithoutIt() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Delete From Notice Host");
            (string name, Guid canoe) = await CreateActivityAsync(eventId, "Canoe Basics");
            (string otherName, Guid swim) = await CreateActivityAsync(eventId, "Swim Check");
            string tab = $"e2e-Morning {Guid.NewGuid():n}";
            await PlaceAndSaveAsync(eventId, canoe, tab);
            await PlaceAndSaveAsync(eventId, swim, tab);

            await Page.GotoAsync(ActivityUrl(eventId, canoe));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Remove placement {tab}" }).ClickAsync();
            await Expect(Page.GetByText($"Delete {name} instead?")).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.GetByText("Its placement is removed.")).ToBeVisibleAsync(InteractiveVisible());
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete activity" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                ActivitiesListUrl(eventId), InteractiveUrl());
            await Expect(Page.GetByText(otherName)).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.GetByText(name)).ToHaveCountAsync(0);
        });

    /// <remarks>
    /// Proves the whole-Activity delete ordering on SQL Server (the Api suite runs on SQLite): the Activity, its
    /// Placements and the Tabs only it kept alive go in a single save. A second, unplaced Activity's autofill is
    /// the witness that the Tabs really are gone, not just hidden from the list.
    /// </remarks>
    [Fact(DisplayName = "Given an Activity placed under two Tabs only it uses, when an Admin deletes it from the list and confirms, then it and both Tabs are gone")]
    public async Task GivenAnActivityPlacedUnderTwoTabsOnlyItUses_WhenAnAdminDeletesItFromTheListAndConfirms_ThenItAndBothTabsAreGone() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Delete From List Host");
            (string name, Guid canoe) = await CreateActivityAsync(eventId, "Canoe Basics");
            (_, Guid swim) = await CreateActivityAsync(eventId, "Swim Check");
            string morning = $"e2e-Morning {Guid.NewGuid():n}";
            string afternoon = $"e2e-Afternoon {Guid.NewGuid():n}";
            await PlaceAndSaveAsync(eventId, canoe, morning, afternoon);

            await Page.GotoAsync(ActivitiesListUrl(eventId));
            await Page.Locator("tr", new PageLocatorOptions { HasText = name })
                .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            await Expect(Page.Locator(".rz-dialog-title")).ToHaveTextAsync("Delete activity?", InteractiveText());
            await Expect(Page.GetByText("Its 2 placements are removed.")).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.GetByText("disappear from the guide too.")).ToBeVisibleAsync(InteractiveVisible());
            await Page.Locator(".rz-dialog").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(Page.GetByText(name)).ToHaveCountAsync(0, InteractiveCount());

            await Page.GotoAsync(ActivityUrl(eventId, swim));
            await Page.Locator("#PlacementTab").ClickAsync();
            await Expect(Page.Locator(".rz-autocomplete-panel").GetByText(morning)).ToHaveCountAsync(0);
            await Expect(Page.Locator(".rz-autocomplete-panel").GetByText(afternoon)).ToHaveCountAsync(0);
        });

    /// <remarks>Places and saves one Tab at a time on the Activity's page, waiting for each save to clear its ghost so the next "Place it" sees a settled tree.</remarks>
    private async Task PlaceAndSaveAsync(Guid eventId, Guid activityId, params string[] tabs)
    {
        await Page.GotoAsync(ActivityUrl(eventId, activityId));
        foreach (string tab in tabs)
        {
            await PlaceAsync(tab);
        }

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());
    }

    private async Task PlaceAsync(string tab)
    {
        await Page.Locator("#PlacementTab").FillAsync(tab);
        await Page.Locator("#PlacementTab").PressAsync("Tab");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
    }

    /// <remarks>Creates through the real new-Activity form. A create lands on the new Activity's own page (P5-8, #94), so the id is read straight from that URL.</remarks>
    private async Task<(string Name, Guid Id)> CreateActivityAsync(Guid eventId, string label)
    {
        string name = $"e2e-{label} {Guid.NewGuid():n}";
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/new").ToString());
        await Page.Locator("#Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex($"/dashboard/events/{eventId}/activities/[0-9a-f-]{{36}}$"),
            InteractiveUrl());

        string path = new Uri(Page.Url).AbsolutePath;
        return (name, Guid.Parse(path[(path.LastIndexOf('/') + 1)..]));
    }

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
