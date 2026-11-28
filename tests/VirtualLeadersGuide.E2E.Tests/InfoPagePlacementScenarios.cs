using System.Text.RegularExpressions;
using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P5-24 (#208, place an InfoPage), one scenario per acceptance criterion plus removal, and what P5-25
/// (#209) shows on the Info pages list. Everything is created through the real UI, and Tiers and Placements need no tracked teardown: deleting the Event removes
/// them (<see cref="E2ETestBase.TrackEvent"/>). Tier names are Guid-suffixed, and <c>RadzenAutoComplete</c> is
/// filled then committed with Tab, as in <see cref="PlacementManagementScenarios"/>.
/// </remarks>
public class InfoPagePlacementScenarios : E2ETestBase
{
    [Fact(DisplayName = "Given an InfoPage with no Placement, when an Admin picks a Tab that Activities use and types a new Sub Tab, then it offers to create only the Sub Tab and saves")]
    public async Task GivenAnUnplacedInfoPage_WhenAnAdminPicksAnExistingTabAndANewSubTab_ThenOnlyTheSubTabIsCreatedAndItSaves() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("InfoPage Placement Host");
            Guid activityId = await CreateActivityAsync(eventId);
            string tab = Unique("Morning");
            await PlaceActivityAsync(eventId, activityId, tab);
            Guid infoPageId = await CreateInfoPageAsync(eventId);

            await OpenEditorAsync(eventId, infoPageId);
            await FillTierAsync("#PlacementTab", tab);
            await Expect(Page.Locator(".ptf-create")).ToHaveCountAsync(0, InteractiveCount());
            await FillTierAsync("#PlacementSubTab", Unique("Check-in"));
            await Expect(Page.Locator(".ptf-create")).ToBeVisibleAsync(InteractiveVisible());

            await SaveToListAsync(eventId);
        });

    [Fact(DisplayName = "Given a saved Placement, when the Admin returns to the InfoPage's edit page, then it shows where the InfoPage is placed")]
    public async Task GivenASavedPlacement_WhenTheAdminReturns_ThenTheEditPageShowsWhereItIsPlaced() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("InfoPage Placed Host");
            Guid infoPageId = await CreateInfoPageAsync(eventId);
            string tab = Unique("Logistics");
            string subTab = Unique("Arrival");

            await PlaceInfoPageAsync(eventId, infoPageId, tab, subTab);
            await OpenEditorAsync(eventId, infoPageId);

            await Expect(Page.Locator(".ipe-placed")).ToContainTextAsync($"{tab} › {subTab}", InteractiveContainText());
        });

    [Fact(DisplayName = "Given a Tab that hosts an Activity, when an Admin places an InfoPage there, then it is rejected, and an Activity is likewise rejected from an InfoPage's Tab")]
    public async Task GivenATabHostingAnActivityOrAnInfoPage_WhenTheOtherIsPlacedThere_ThenItIsRejected() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Exclusive Screen Host");
            Guid activityId = await CreateActivityAsync(eventId);
            string activityTab = Unique("Morning");
            await PlaceActivityAsync(eventId, activityId, activityTab);
            Guid infoPageId = await CreateInfoPageAsync(eventId);

            await OpenEditorAsync(eventId, infoPageId);
            await FillTierAsync("#PlacementTab", activityTab);
            await SaveAsync();
            await Expect(Page.GetByText($"{activityTab} already shows Activities")).ToBeVisibleAsync(InteractiveVisible());

            string infoPageTab = Unique("About");
            await PlaceInfoPageAsync(eventId, infoPageId, infoPageTab);
            await Page.GotoAsync(ActivityUrl(eventId, activityId));
            await FillTierAsync("#PlacementTab", infoPageTab);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
            await SaveAsync();

            await Expect(Page.GetByText("an Info page is shown there")).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Tab that hosts an InfoPage, when an Admin places a different InfoPage there, then it is rejected")]
    public async Task GivenATabHostingAnInfoPage_WhenASecondInfoPageIsPlacedThere_ThenItIsRejected() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("One InfoPage Host");
            Guid first = await CreateInfoPageAsync(eventId);
            Guid second = await CreateInfoPageAsync(eventId);
            string tab = Unique("Packing");
            await PlaceInfoPageAsync(eventId, first, tab);

            await OpenEditorAsync(eventId, second);
            await FillTierAsync("#PlacementTab", tab);
            await SaveAsync();

            await Expect(Page.GetByText($"{tab} already shows another Info page")).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given a placed InfoPage, when the Admin opens it, then no second picker is offered, and Change moves the one Placement")]
    public async Task GivenAPlacedInfoPage_WhenTheAdminOpensIt_ThenNoSecondPickerIsOfferedAndChangeMovesIt() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Move InfoPage Host");
            Guid infoPageId = await CreateInfoPageAsync(eventId);
            string from = Unique("Before");
            string to = Unique("After");
            await PlaceInfoPageAsync(eventId, infoPageId, from);

            await OpenEditorAsync(eventId, infoPageId);
            await Expect(Page.Locator(".ipe-placed")).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.Locator("#PlacementTab")).ToHaveCountAsync(0);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Change", Exact = true }).ClickAsync();
            await FillTierAsync("#PlacementTab", to);
            await SaveToListAsync(eventId);

            await OpenEditorAsync(eventId, infoPageId);
            await Expect(Page.Locator(".ipe-placed")).ToContainTextAsync(to, InteractiveContainText());
        });

    [Fact(DisplayName = "Given a Director not assigned to an Event, when navigating to one of its InfoPages to place it, then they are denied")]
    public async Task GivenADirectorNotAssignedToAnEvent_WhenOpeningItsInfoPage_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned For Placement");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned For Placement");
            Guid infoPageId = await CreateInfoPageAsync(unassignedEventId);
            await SignOutAsync();
            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(EditorUrl(unassignedEventId, infoPageId));

            await Expect(Page.GetByText("You don't have access to this Info page")).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given a placed InfoPage alone on its Tab, when the Admin removes the Placement, then they confirm losing the Tab and the page is unplaced")]
    public async Task GivenAPlacedInfoPageAloneOnItsTab_WhenTheAdminRemovesIt_ThenTheyConfirmAndItIsUnplaced() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Remove InfoPage Host");
            Guid infoPageId = await CreateInfoPageAsync(eventId);
            string tab = Unique("Temporary");
            await PlaceInfoPageAsync(eventId, infoPageId, tab);

            await OpenEditorAsync(eventId, infoPageId);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("the tab disappears from the guide too")).ToBeVisibleAsync(InteractiveVisible());
            await Page.Locator(".rz-dialog")
                .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove", Exact = true }).ClickAsync();

            await Expect(Page.Locator("#PlacementTab")).ToBeVisibleAsync(InteractiveVisible());
            await Page.ReloadAsync();
            await Expect(Page.Locator("#PlacementTab")).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given a placed and an unplaced InfoPage, when the Admin opens the Info pages list, then each row shows its Tab › Sub Tab or Not placed")]
    public async Task GivenAPlacedAndAnUnplacedInfoPage_WhenTheAdminOpensTheList_ThenEachRowShowsWhereItIs() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("InfoPage List Host");
            string placedTitle = Unique("Packing");
            string unplacedTitle = Unique("Draft");
            Guid placed = await CreateInfoPageAsync(eventId, placedTitle);
            await CreateInfoPageAsync(eventId, unplacedTitle);
            string tab = Unique("Logistics");
            string subTab = Unique("Arrival");

            await PlaceInfoPageAsync(eventId, placed, tab, subTab);

            await Expect(Row(placedTitle)).ToContainTextAsync($"{tab} › {subTab}", InteractiveContainText());
            await Expect(Row(unplacedTitle)).ToContainTextAsync("Not placed", InteractiveContainText());
        });

    [Fact(DisplayName = "Given a placed InfoPage, when the Admin moves it, then the list shows the new Tab › Sub Tab")]
    public async Task GivenAPlacedInfoPage_WhenTheAdminMovesIt_ThenTheListShowsTheNewPlace() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("InfoPage List Move Host");
            string title = Unique("Page");
            Guid infoPageId = await CreateInfoPageAsync(eventId, title);
            await PlaceInfoPageAsync(eventId, infoPageId, Unique("Before"));
            string tab = Unique("After");
            string subTab = Unique("Below");

            await OpenEditorAsync(eventId, infoPageId);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Change", Exact = true }).ClickAsync();
            await FillTierAsync("#PlacementTab", tab);
            await FillTierAsync("#PlacementSubTab", subTab);
            await SaveToListAsync(eventId);

            await Expect(Row(title)).ToContainTextAsync($"{tab} › {subTab}", InteractiveContainText());
        });

    [Fact(DisplayName = "Given a placed InfoPage, when the Admin removes its Placement, then the list shows Not placed")]
    public async Task GivenAPlacedInfoPage_WhenTheAdminRemovesItsPlacement_ThenTheListShowsNotPlaced() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("InfoPage List Remove Host");
            string title = Unique("Page");
            Guid infoPageId = await CreateInfoPageAsync(eventId, title);
            await PlaceInfoPageAsync(eventId, infoPageId, Unique("Temporary"));

            await OpenEditorAsync(eventId, infoPageId);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove", Exact = true }).ClickAsync();
            await Page.Locator(".rz-dialog")
                .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove", Exact = true }).ClickAsync();
            await Expect(Page.Locator("#PlacementTab")).ToBeVisibleAsync(InteractiveVisible());
            await Page.GotoAsync(ListUrl(eventId));

            await Expect(Row(title)).ToContainTextAsync("Not placed", InteractiveContainText());
        });

    [Fact(DisplayName = "Given a placed InfoPage alone on its Tab, when the Admin deletes it from the list, then the confirmation says the tab disappears too")]
    public async Task GivenAPlacedInfoPageAloneOnItsTab_WhenTheAdminDeletesItFromTheList_ThenTheConfirmationNamesTheTab() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("InfoPage List Delete Host");
            string title = Unique("Page");
            Guid infoPageId = await CreateInfoPageAsync(eventId, title);
            string tab = Unique("Solo");
            await PlaceInfoPageAsync(eventId, infoPageId, tab);

            await Row(title).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog");
            await Expect(dialog).ToContainTextAsync($"It's removed from {tab}.", InteractiveContainText());
            await Expect(dialog).ToContainTextAsync($"The {tab} tab disappears from the guide too.", InteractiveContainText());
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(Row(title)).ToHaveCountAsync(0, InteractiveCount());
        });

    private ILocator Row(string title) => Page.Locator("tr", new PageLocatorOptions { HasText = title });

    private string ListUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages").ToString();

    private static string Unique(string label) => $"e2e-{label} {Guid.NewGuid():n}";

    private async Task FillTierAsync(string selector, string value)
    {
        await Page.Locator(selector).FillAsync(value);
        await Page.Locator(selector).PressAsync("Tab");
    }

    private async Task SaveAsync() =>
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

    /// <remarks>A successful save lands on the list; waiting for it keeps the next navigation from cutting the write off.</remarks>
    private async Task SaveToListAsync(Guid eventId)
    {
        await SaveAsync();
        await Expect(Page).ToHaveURLAsync(
            new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages").ToString(), InteractiveUrl());
    }

    private async Task OpenEditorAsync(Guid eventId, Guid infoPageId)
    {
        await Page.GotoAsync(EditorUrl(eventId, infoPageId));
        await Expect(Page.Locator(".ipe-placement")).ToBeVisibleAsync(InteractiveVisible());
    }

    /// <remarks>Places an unplaced InfoPage on a screen that is free.</remarks>
    private async Task PlaceInfoPageAsync(Guid eventId, Guid infoPageId, string tab, string? subTab = null)
    {
        await OpenEditorAsync(eventId, infoPageId);
        await FillTierAsync("#PlacementTab", tab);
        if (subTab is not null)
        {
            await FillTierAsync("#PlacementSubTab", subTab);
        }

        await SaveToListAsync(eventId);
    }

    private async Task PlaceActivityAsync(Guid eventId, Guid activityId, string tab)
    {
        await Page.GotoAsync(ActivityUrl(eventId, activityId));
        await FillTierAsync("#PlacementTab", tab);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
        await SaveAsync();
        await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());
    }

    private async Task<Guid> CreateActivityAsync(Guid eventId)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/new").ToString());
        await Page.Locator("#Name").FillAsync(Unique("Canoe Basics"));
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/dashboard/events/{eventId}/activities/[0-9a-f-]{{36}}$"), InteractiveUrl());
        return LastSegment(Page.Url);
    }

    private async Task<Guid> CreateInfoPageAsync(Guid eventId, string? title = null)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages/new").ToString());
        await Page.Locator("#Title").FillAsync(title ?? Unique("Page"));
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create page" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"info-pages/[0-9a-f-]{36}$"), InteractiveUrl());
        return LastSegment(Page.Url);
    }

    private static Guid LastSegment(string url)
    {
        string path = new Uri(url).AbsolutePath;
        return Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    private string EditorUrl(Guid eventId, Guid infoPageId) =>
        new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages/{infoPageId}").ToString();

    private string ActivityUrl(Guid eventId, Guid activityId) =>
        new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/{activityId}").ToString();

    /// <remarks>Kept local, not shared - see <see cref="InfoPageManagementScenarios"/>'s identical copy.</remarks>
    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        IdentityUserDto director = await CreateTrackedUserAsync("e2e-director", CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, director.Email!, TestCredentials.KnownPassword);
        return director;
    }
}
