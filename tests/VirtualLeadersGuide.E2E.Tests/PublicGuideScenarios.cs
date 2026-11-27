using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Covers P4-2 (#72): the public entry path at <c>/</c> and <c>/e/{slug}</c> - event lookup, passcode entry,
/// and the landing states around it (wrong passcode, unknown address, Cancelled/Draft, the signed-in staff
/// bypass, and a Passcode rotation revoking an existing Unlock). Also covers P5-14 (#88): once unlocked, the
/// guide shows its content behind the Tabs in the top bar (#241).
/// </summary>
/// <remarks>
/// Every Event here goes through the real UI (<see cref="E2ETestBase.CreateEventAsync"/>), same discipline as
/// <c>EventManagementScenarios</c> - Slug/Passcode are read directly off the Event editor's own form
/// (<c>#Slug</c>/<c>#Passcode</c>), never re-derived, matching that class's own Duplicate coverage. A scenario
/// that needs an anonymous visitor signs out first (<see cref="E2ETestBase.SignOutAsync"/>) - the Unlock
/// cookie this suite writes is a plain cookie, unrelated to Web's Identity sign-in cookie - two separate
/// cookie concerns by design (ADR-0058), so signing in/out around it never disturbs it within one test's
/// single browser context.
/// </remarks>
public class PublicGuideScenarios : E2ETestBase
{
    [Fact(DisplayName = "Given a Live Event's address and correct passcode, when a visitor submits them on /, then they reach the unlocked guide")]
    public async Task GivenALiveEventsAddressAndCorrectPasscode_WhenAVisitorSubmitsThemOnHome_ThenTheyReachTheUnlockedGuide() =>
        await RunAsync(async () =>
        {
            (string slug, string passcode) = await CreateAndPublishEventAsync("Summer Camporee");
            await SignOutAsync();

            await SubmitOnHomeAsync(slug, passcode);

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());
            await Expect(UnlockedHeading()).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Live Event's address but the wrong passcode, when a visitor submits them on /, then they see an inline error and stay on /")]
    public async Task GivenALiveEventsAddressButTheWrongPasscode_WhenAVisitorSubmitsThemOnHome_ThenTheySeeAnInlineErrorAndStayOnHome() =>
        await RunAsync(async () =>
        {
            (string slug, _) = await CreateAndPublishEventAsync("Fall Webelos Woods");
            await SignOutAsync();

            await SubmitOnHomeAsync(slug, "DefinitelyWrongGuess");

            await Expect(Page).ToHaveURLAsync(Fixture.WebBaseUrl.ToString());
            await Expect(Page.GetByText("doesn't match this event")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given no Event exists at an address, when a visitor submits it on /, then they see the not-found error")]
    public async Task GivenNoEventExistsAtAnAddress_WhenAVisitorSubmitsItOnHome_ThenTheySeeTheNotFoundError() =>
        await RunAsync(async () =>
        {
            await SubmitOnHomeAsync($"no-such-event-{Guid.NewGuid():n}", "anything");

            await Expect(Page.GetByText("We couldn't find an event at that address")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Cancelled Event, when a visitor reaches /e/{slug} with the correct passcode, then they see the cancelled state instead of the guide")]
    public async Task GivenACancelledEvent_WhenAVisitorReachesItWithTheCorrectPasscode_ThenTheySeeTheCancelledState() =>
        await RunAsync(async () =>
        {
            (string slug, string passcode) = await CreateAndPublishEventAsync("Cancelled Camporee");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel event", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("CANCELLED", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(InteractiveVisible());
            await SignOutAsync();

            await SubmitOnHomeAsync(slug, passcode);

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());
            await Expect(Page.GetByText("This event was cancelled")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Draft Event, when a visitor reaches its address directly at /e/{slug}, then they see the not-found state")]
    public async Task GivenADraftEvent_WhenAVisitorReachesItsAddressDirectlyAtE_ThenTheySeeTheNotFoundState() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Still Planning");
            await Page.GotoAsync(EventEditorUrl(eventId));
            string slug = await Page.Locator("#Slug").InputValueAsync();
            await SignOutAsync();

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());

            await Expect(Page.GetByText("We couldn't find an event at that address")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a signed-in Admin, when they visit their own Event's /e/{slug} with no Unlock cookie and no passcode entered, then they land on the unlocked guide")]
    public async Task GivenASignedInAdmin_WhenTheyVisitTheirOwnEventsAddress_ThenTheyLandOnTheUnlockedGuideWithNoPasscode() =>
        await RunAsync(async () =>
        {
            (string slug, _) = await CreateAndPublishEventAsync("Staff Preview");

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());

            await Expect(UnlockedHeading()).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a visitor who already unlocked an Event's guide, when an Admin then rotates its Passcode, then the visitor is locked again on their next visit")]
    public async Task GivenAVisitorWhoAlreadyUnlockedAGuide_WhenAnAdminRotatesThePasscode_ThenTheVisitorIsLockedAgainOnTheirNextVisit() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, string passcode) = await CreateAndPublishEventReturningIdAsync("Rotate Me");
            await SignOutAsync();
            await SubmitOnHomeAsync(slug, passcode);
            await Expect(UnlockedHeading()).ToBeVisibleAsync();

            await SignInAsAdminAsync();
            string newPasscode = $"Testcode{Guid.NewGuid():n}";
            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#Passcode").FillAsync(newPasscode);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString(), InteractiveUrl());
            await SignOutAsync();

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());

            await Expect(Page.GetByText("Unlock this guide")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given Activities under a Tab, a Section and a Sub Section and an InfoPage on a second Tab, when a visitor unlocks and picks the second Tab, then each screen shows its own content in order")]
    public async Task GivenActivitiesOnOneTabAndAnInfoPageOnAnother_WhenAVisitorPicksTheSecondTab_ThenEachScreenShowsItsContentInOrder() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, string passcode) = await CreateAndPublishEventReturningIdAsync("Guide Tabs");
            const string day = "Day";
            const string packing = "Packing";
            string section = Unique("Ranges");
            string subSection = Unique("Archery");
            string loose = await CreatePlacedActivityAsync(eventId, "Opening", day);
            string sectioned = await CreatePlacedActivityAsync(eventId, "Safety Talk", day, section: section);
            string nested = await CreatePlacedActivityAsync(eventId, "Far Butts", day, section: section, subSection: subSection);
            string title = await CreatePlacedInfoPageAsync(eventId, "Welcome to **camp**!", packing);
            await SignOutAsync();

            await SubmitOnHomeAsync(slug, passcode);

            await Expect(Page.Locator(".vlg-screen").Locator("h2, h3, h4"))
                .ToHaveTextAsync([loose, section, sectioned, subSection, nested]);

            await TopBarTabs().GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = packing }).ClickAsync();

            await Expect(Page.Locator(".vlg-screen h2")).ToHaveTextAsync(title);
            await Expect(Page.Locator(".vlg-screen strong")).ToHaveTextAsync("camp");
            await Expect(Page.Locator(".vlg-screen article")).ToHaveCountAsync(0);
        });

    [Fact(DisplayName = "Given a Tab with a directly placed Activity and a Sub Tab, when a visitor picks the Sub Tab and then the Tab-named entry, then each screen shows only its own Activities")]
    public async Task GivenATabWithBareContentAndASubTab_WhenAVisitorSwitchesBetweenThem_ThenEachScreenShowsOnlyItsOwnActivities() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, string passcode) = await CreateAndPublishEventReturningIdAsync("Guide Sub Tabs");
            string day = Unique("Day");
            string roundRobin = Unique("Round Robin");
            string loose = await CreatePlacedActivityAsync(eventId, "Opening", day);
            string rotation = await CreatePlacedActivityAsync(eventId, "Rotation", day, subTab: roundRobin);
            await SignOutAsync();
            await SubmitOnHomeAsync(slug, passcode);
            ILocator dropdown = TopBarTabs().Locator("summary", new LocatorLocatorOptions { HasText = day });
            ILocator activities = Page.Locator(".vlg-screen h3");

            await Expect(activities).ToHaveTextAsync(loose);

            await dropdown.ClickAsync();
            await TopBarTabs().GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = roundRobin }).ClickAsync();
            await Expect(activities).ToHaveTextAsync(rotation);

            await dropdown.ClickAsync();
            await TopBarTabs().GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = day }).ClickAsync();
            await Expect(activities).ToHaveTextAsync(loose);
        });

    /// <remarks>Only a real browser shows this: Blazor's enhanced navigation keeps the scroll position when only the query string changes.</remarks>
    [Fact(DisplayName = "Given a visitor scrolled down a long screen on a phone, when they pick another Tab, then the new screen starts at the top")]
    public async Task GivenAVisitorScrolledDownALongScreen_WhenTheyPickAnotherTab_ThenTheNewScreenStartsAtTheTop() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, string passcode) = await CreateAndPublishEventReturningIdAsync("Guide Scroll");
            string longContent = string.Join("\n\n", Enumerable.Range(1, 60).Select(n => $"Paragraph {n}"));
            await CreatePlacedInfoPageAsync(eventId, longContent, Unique("A First"));
            string secondTab = Unique("B Second");
            string secondTitle = await CreatePlacedInfoPageAsync(eventId, longContent, secondTab);
            await SignOutAsync();
            await SubmitOnHomeAsync(slug, passcode);
            await Expect(UnlockedHeading()).ToBeVisibleAsync();
            await Page.SetViewportSizeAsync(375, 600);
            await Page.EvaluateAsync("window.scrollTo(0, 1500)");
            Assert.Equal(0, (await Page.Locator("header").BoundingBoxAsync())!.Y);

            await GuideMenuButton().ClickAsync();
            LocatorBoundingBoxResult panel = (await GuideMenu().BoundingBoxAsync())!;
            Assert.True(panel.X >= 0 && panel.X + panel.Width <= 375, $"The menu runs off the screen: {panel.X} + {panel.Width}");
            await GuideMenu().GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = secondTab }).ClickAsync();

            await Expect(Page.Locator(".vlg-screen h2")).ToHaveTextAsync(secondTitle);
            await Page.WaitForFunctionAsync("window.scrollY === 0", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        });

    [Fact(DisplayName = "Given an Event with nothing placed, when a visitor unlocks its guide, then they see the empty-guide message")]
    public async Task GivenAnEventWithNothingPlaced_WhenAVisitorUnlocksItsGuide_ThenTheySeeTheEmptyGuideMessage() =>
        await RunAsync(async () =>
        {
            (string slug, string passcode) = await CreateAndPublishEventAsync("Empty Guide Host");
            await SignOutAsync();

            await SubmitOnHomeAsync(slug, passcode);

            await Expect(Page.GetByText("haven't been added yet")).ToBeVisibleAsync();
        });

    /// <remarks>Only a real browser runs <c>menus.js</c> and shows which of the two navigations CSS displays.</remarks>
    [Fact(DisplayName = "Given a signed-in Admin on a guide, when they open a dropdown and click elsewhere, then it closes and no management links are offered")]
    public async Task GivenASignedInAdminOnAGuide_WhenTheyClickOutsideAnOpenDropdown_ThenItClosesAndNoManagementLinksShow() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, _) = await CreateAndPublishEventReturningIdAsync("Guide Staff");
            string day = Unique("Day");
            string roundRobin = Unique("Round Robin");
            await CreatePlacedActivityAsync(eventId, "Rotation", day, subTab: roundRobin);
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());
            ILocator subTabLink = TopBarTabs().GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = roundRobin });

            await Expect(AccountMenuButton).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = "Main" })).ToHaveCountAsync(0);
            await Expect(GuideMenuButton()).ToBeHiddenAsync();
            await TopBarTabs().Locator("summary").ClickAsync();
            await Expect(subTabLink).ToBeVisibleAsync();

            await Page.Mouse.ClickAsync(1200, 500);

            await Expect(subTabLink).ToBeHiddenAsync();
        });

    /// <remarks>Only a real browser shows how tall the bar is, where a menu lands and where focus goes.</remarks>
    [Fact(DisplayName = "Given a guide with more Tabs than fit, when a visitor uses More by keyboard, then the bar stays one row, the menu stays on screen and Escape returns focus to it")]
    public async Task GivenMoreTabsThanFit_WhenAVisitorUsesMore_ThenTheBarStaysOneRowAndFocusReturns() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, _) = await CreateAndPublishEventReturningIdAsync("Guide Overflow");
            string[] tabs = ["Day", "Packing and Gear", "Saturday Morning Program", "Maps and Directions", "Health and Safety"];
            foreach (string tab in tabs)
            {
                await CreatePlacedActivityAsync(eventId, "Item", tab);
            }

            await Page.SetViewportSizeAsync(1040, 720);
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());
            ILocator more = TopBarTabs().Locator("summary", new LocatorLocatorOptions { HasText = "More" });
            ILocator last = TopBarTabs().GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = tabs[^1] });

            Assert.True((await Page.Locator("header").BoundingBoxAsync())!.Height < 90, "The top bar wrapped.");
            await more.PressAsync("Enter");
            LocatorBoundingBoxResult box = (await last.BoundingBoxAsync())!;
            Assert.True(box.X >= 0 && box.X + box.Width <= 1040, $"The menu runs off the screen: {box.X} + {box.Width}");

            await Page.Keyboard.PressAsync("Tab");
            await Page.Keyboard.PressAsync("Escape");
            await Expect(last).ToBeHiddenAsync();
            await Expect(more).ToBeFocusedAsync();

            await more.PressAsync("Enter");
            await Page.Locator("header a.vlg-brand").FocusAsync();
            await Expect(last).ToBeHiddenAsync();

            await more.PressAsync("Enter");
            await last.ClickAsync();
            await Expect(Page.Locator(".vlg-guide-location")).ToHaveTextAsync(tabs[^1]);
        });

    private ILocator TopBarTabs() => Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = "Tabs" });

    private ILocator GuideMenuButton() => Page.Locator("header summary[aria-label='Guide menu']");

    private ILocator GuideMenu() => Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = "Guide menu" });

    private static string Unique(string label) => $"e2e-{label} {Guid.NewGuid():n}";

    /// <remarks>The heading only the unlocked state renders.</remarks>
    private ILocator UnlockedHeading() => Page.Locator(".vlg-guide-heading");

    private async Task FillTierAsync(string selector, string? value)
    {
        if (value is not null)
        {
            await Page.Locator(selector).FillAsync(value);
            await Page.Locator(selector).PressAsync("Tab");
        }
    }

    /// <remarks>Creates and places through the real forms, as <c>PlacementManagementScenarios</c> does; assumes an Admin is signed in.</remarks>
    private async Task<string> CreatePlacedActivityAsync(
        Guid eventId, string label, string tab, string? subTab = null, string? section = null, string? subSection = null)
    {
        string name = Unique(label);
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/new").ToString());
        await Page.Locator("#Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/dashboard/events/{eventId}/activities/[0-9a-f-]{{36}}$"), InteractiveUrl());

        await Expect(Page.Locator("#PlacementTab")).ToBeVisibleAsync(InteractiveVisible());
        await FillTierAsync("#PlacementTab", tab);
        await FillTierAsync("#PlacementSubTab", subTab);
        await FillTierAsync("#PlacementSection", section);
        await FillTierAsync("#PlacementSubSection", subSection);
        await Expect(Page.Locator(".pb-path")).ToContainTextAsync(subSection ?? section ?? subTab ?? tab, InteractiveContainText());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Place it" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(Page.Locator(".ptn-pending")).ToHaveCountAsync(0, InteractiveCount());

        return name;
    }

    /// <remarks>Saving a Placement lands on the InfoPage list; waiting for it keeps the next navigation from cutting the write off.</remarks>
    private async Task<string> CreatePlacedInfoPageAsync(Guid eventId, string markdownContent, string tab)
    {
        string title = Unique("About");

        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages/new").ToString());
        await Page.Locator("#Title").FillAsync(title);
        await Page.Locator("#MarkdownContent").FillAsync(markdownContent);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create page" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"info-pages/[0-9a-f-]{36}$"), InteractiveUrl());

        await Expect(Page.Locator(".ipe-placement")).ToBeVisibleAsync(InteractiveVisible());
        await FillTierAsync("#PlacementTab", tab);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages").ToString(), InteractiveUrl());

        return title;
    }

    /// <remarks>
    /// Signs in, creates the Event, goes live, reads its real Slug/Passcode off the editor form, and leaves
    /// the session signed in as Admin - callers that need an anonymous visitor afterward call
    /// <see cref="E2ETestBase.SignOutAsync"/> themselves once they're done with anything staff-only.
    /// </remarks>
    private async Task<(string Slug, string Passcode)> CreateAndPublishEventAsync(string label)
    {
        (_, string slug, string passcode) = await CreateAndPublishEventReturningIdAsync(label);
        return (slug, passcode);
    }

    private async Task<(Guid Id, string Slug, string Passcode)> CreateAndPublishEventReturningIdAsync(string label)
    {
        await SignInAsAdminAsync();
        (Guid eventId, _) = await CreateEventAsync(label);

        await Page.GotoAsync(EventEditorUrl(eventId));
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
        await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(InteractiveVisible());

        string slug = await Page.Locator("#Slug").InputValueAsync();
        string passcode = await Page.Locator("#Passcode").InputValueAsync();
        return (eventId, slug, passcode);
    }

    private async Task SubmitOnHomeAsync(string address, string passcode)
    {
        await Page.GotoAsync(Fixture.WebBaseUrl.ToString());
        await Page.Locator("#Input\\.Address").FillAsync(address);
        await Page.Locator("#Input\\.Passcode").FillAsync(passcode);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open guide" }).ClickAsync();
    }
}
