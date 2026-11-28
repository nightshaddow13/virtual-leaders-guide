using System.Text.RegularExpressions;
using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P2-9 (#18): Admin create/edit over <c>/dashboard/events</c>, and the read-only/denied states a
/// Director sees per ADR-0031's narrowing (Event details are Admin-only to write). Every Event this class
/// creates goes through the real UI, not a seeding helper - there's no Events API helper here the way
/// <see cref="IdentityApiClient"/> seeds Identity, since the whole point of these scenarios is that the
/// create flow itself works. <see cref="IdentityApiClient.GrantDirectorAsync"/> is the one exception
/// (assigning a Director), since driving the Users screen that would do this doesn't exist yet (P2-10, #19).
/// Every Director here is its own throwaway account, not <see cref="AspireE2EFixture.DirectorEmail"/> - see
/// ADR-0039 for why a test whose subject is "what can this Director see" stays isolated even where nothing
/// today would technically break by sharing.
/// </remarks>
/// <remarks>
/// Status coverage (P2-20, #115) - three facts worth stating once rather than per scenario: (1) every
/// <c>GetByText</c> against a badge word ("LIVE"/"PAST"/"CANCELLED") passes <c>Exact = true</c>, since a
/// guid-suffixed Event name that happens to contain the same substring (e.g. label "Go Live" -&gt; Name
/// "e2e-Go Live &lt;guid&gt;", whose uppercased breadcrumb literally contains "LIVE") otherwise collides with
/// the badge under Playwright's strict mode - confirmed the hard way, an earlier draft without <c>Exact</c>
/// failed exactly this way. (2) The STATUS filter dropdown interaction always waits for the dropdown, then
/// the target option, to be visible before each click - a fresh <c>GotoAsync</c> plus a negative assertion
/// (something isn't shown) doesn't guarantee the grid's interactive circuit has finished hydrating the way a
/// positive wait does, unlike every other interaction in this class. (3) The elapsed-Live-shows-Past scenario
/// is the one AC the SQLite-backed <c>EventsResourceShould</c> suite structurally cannot verify at the
/// collection level - EF Core's SQLite provider has never translated a <see cref="DateTimeOffset"/> inequality
/// comparison, so default-list-hides-an-elapsed-Live-Event is only provable against the real engine here (see
/// <c>EventStatusFilterRewriter</c>'s remarks and ADR-0053); that scenario also reloads the editor after going
/// live rather than asserting on the immediate post-click state, since <c>EventEditor.razor.cs</c>'s
/// <c>GoLiveAsync</c> optimistically sets <c>Live</c> locally without re-fetching, so only a fresh
/// <c>GET</c> exercises Api's own <c>OnSerialize</c> computing <c>Past</c>.
/// </remarks>
/// <remarks>
/// Duplicate coverage (P2-21, #116, ADR-0054) - proves the full deviation from the issue's own filed AC in
/// one pass: the source's dates land pre-filled but its Name doesn't (the Admin types a fresh one), the
/// source going Live first proves the duplicate is Draft regardless of the source's Status (nothing here ever
/// sets the duplicate's Status directly - it's Draft because nothing ever moved it), and Slug/Passcode are
/// read directly off the source's own edit form rather than re-derived, so the "never the source's" comparison
/// is against what the source actually has, not an assumption about what it should be.
/// </remarks>
/// <remarks>
/// Every dashboard row lookup narrows the grid by the Event's own guid-suffixed name first (P2.1-8, #205):
/// other classes create Events concurrently and can push a row off the first page. The upper-cased search
/// scenario is also the only proof the name filter ignores case - the SQLite-backed Api suite can't give it
/// (ADR-0053).
/// </remarks>
public class EventManagementScenarios : E2ETestBase
{
    [Fact(DisplayName = "Given a signed-in Admin, when creating an Event with only a name, then it appears in the Events list")]
    public async Task GivenASignedInAdmin_WhenCreatingAnEventWithOnlyAName_ThenItAppearsInTheEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Summer Camporee");

            await GoToDashboardFilteredByNameAsync(name);
            await Expect(EventRow(name)).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin edits its name, address, and passcode, then the changes persist")]
    public async Task GivenAnExistingEvent_WhenAnAdminEditsItsNameAddressAndPasscode_ThenTheChangesPersist() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Fall Webelos Woods");

            string renamedTo = $"e2e-Renamed {Guid.NewGuid():n}";
            string newSlug = $"e2e-renamed-{Guid.NewGuid():n}";
            string newPasscode = $"Testcode{Guid.NewGuid():n}";

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#Name").FillAsync(renamedTo);
            await Page.Locator("#Slug").FillAsync(newSlug);
            await Page.Locator("#Passcode").FillAsync(newPasscode);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard").ToString(),
                InteractiveUrl());

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Expect(Page.Locator("#Name")).ToHaveValueAsync(renamedTo);
            await Expect(Page.Locator("#Slug")).ToHaveValueAsync(newSlug);
            await Expect(Page.Locator("#Passcode")).ToHaveValueAsync(newPasscode);
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin sets its start and end, then the dashboard grid shows the formatted range")]
    public async Task GivenAnExistingEvent_WhenAnAdminSetsItsStartAndEnd_ThenTheDashboardGridShowsTheFormattedRange() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Summer Camporee");

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#StartsAt").FillAsync("2026-06-12T09:00");
            await Page.Locator("#EndsAt").FillAsync("2026-06-14T17:00");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard").ToString(),
                InteractiveUrl());

            await FilterDashboardByNameAsync(name);
            ILocator row = EventRow(name);
            await Expect(row.GetByText("JUN 12")).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin sets an end before the start, then the error lands on the end field")]
    public async Task GivenAnExistingEvent_WhenAnAdminSetsAnEndBeforeTheStart_ThenTheErrorLandsOnTheEndField() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Winter Klondike");

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#StartsAt").FillAsync("2026-06-14T09:00");
            await Page.Locator("#EndsAt").FillAsync("2026-06-12T09:00");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(EventEditorUrl(eventId));
            await Expect(FieldErrorLocator("EndsAt").GetByText("End must be after the start.")).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin creates another with the same name, then the error lands on the name field")]
    public async Task GivenAnExistingEvent_WhenAnAdminCreatesAnotherWithTheSameName_ThenTheErrorLandsOnTheNameField() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Winter Klondike");

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());
            await Page.Locator("#Name").FillAsync(name);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create event" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());

            await Expect(FieldErrorLocator("Name").GetByText("already in use by another Event")).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin deletes it and confirms, then it is removed from the Events list")]
    public async Task GivenAnExistingEvent_WhenAnAdminDeletesItAndConfirms_ThenItIsRemovedFromTheEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("To Delete");

            await GoToDashboardFilteredByNameAsync(name);
            ILocator row = EventRow(name);
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();

            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(row).Not.ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given the delete dialog is open, when an Admin cancels instead, then the Event remains listed")]
    public async Task GivenTheDeleteDialogIsOpen_WhenAnAdminCancelsInstead_ThenTheEventRemainsListed() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Not Deleted");

            await GoToDashboardFilteredByNameAsync(name);
            ILocator row = EventRow(name);
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();

            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();

            await Expect(dialog).Not.ToBeVisibleAsync();
            await Expect(EventRow(name)).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Director assigned to one Event, when viewing the dashboard, then only that Event is listed read-only with no create button")]
    public async Task GivenADirectorAssignedToOneEvent_WhenViewingTheDashboard_ThenOnlyThatEventIsListedReadOnlyWithNoCreateButton() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, string assignedName) = await CreateEventAsync("Spring Klondike");
            (_, string unassignedName) = await CreateEventAsync("Not Assigned");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await GoToDashboardFilteredByNameAsync(assignedName);

            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My events", Exact = true }))
                .ToBeVisibleAsync(InteractiveVisible());
            await Expect(EventRow(assignedName)).ToBeVisibleAsync(
                InteractiveVisible());
            await FilterDashboardByNameAsync(unassignedName);
            await Expect(NoMatchMessage()).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ New event" })).Not.ToBeVisibleAsync();

            await Page.GotoAsync(EventEditorUrl(assignedEventId));
            await Expect(Page.GetByText("VIEW ONLY")).ToBeVisibleAsync(
                InteractiveVisible());
            await Expect(Page.Locator("#Name")).Not.ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" })).Not.ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Director assigned to an Event, when viewing the dashboard or that Event, then no delete action is available")]
    public async Task GivenADirectorAssignedToAnEvent_WhenViewingTheDashboardOrThatEvent_ThenNoDeleteActionIsAvailable() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, string assignedName) = await CreateEventAsync("Director Viewable");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await GoToDashboardFilteredByNameAsync(assignedName);
            ILocator row = EventRow(assignedName);
            await Expect(row).ToBeVisibleAsync(InteractiveVisible());
            await Expect(row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" })).Not.ToBeVisibleAsync();

            await Page.GotoAsync(EventEditorUrl(assignedEventId));
            await Expect(Page.GetByText("VIEW ONLY")).ToBeVisibleAsync(
                InteractiveVisible());
            await Expect(Page.GetByText("Danger zone")).Not.ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Director, when navigating directly to an Event they aren't assigned to, then they are denied")]
    public async Task GivenADirector_WhenNavigatingDirectlyToAnEventTheyArentAssignedTo_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(EventEditorUrl(unassignedEventId));

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Director, when navigating directly to the new-Event page, then they are denied without a form ever rendering")]
    public async Task GivenADirector_WhenNavigatingDirectlyToTheNewEventPage_ThenTheyAreDeniedWithoutAFormEverRendering() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                InteractiveVisible());
            await Expect(Page.Locator("#Name")).Not.ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Draft Event, when an Admin marks it Live, then the dashboard shows a LIVE badge")]
    public async Task GivenADraftEvent_WhenAnAdminMarksItLive_ThenTheDashboardShowsALiveBadge() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Go Live");

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await GoToDashboardFilteredByNameAsync(name);
            ILocator row = EventRow(name);
            await Expect(row.GetByText("LIVE", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Live Event, when an Admin cancels it through the Danger zone, then it leaves the default list but is reachable by filtering to Cancelled")]
    public async Task GivenALiveEvent_WhenAnAdminCancelsItThroughTheDangerZone_ThenItLeavesTheDefaultListButIsReachableByFilteringToCancelled() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Cancel Me");
            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
            await Expect(dialog.GetByText($"Cancel {name}?")).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel event", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("CANCELLED", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await GoToDashboardFilteredByNameAsync(name);
            await Expect(NoMatchMessage()).ToBeVisibleAsync(InteractiveVisible());

            await FilterDashboardByStatusAsync("Cancelled");

            ILocator row = EventRow(name);
            await Expect(row).ToBeVisibleAsync(InteractiveVisible());
            await Expect(row.GetByText("CANCELLED", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given the cancel dialog is open, when an Admin dismisses it instead, then the Event stays Live")]
    public async Task GivenTheCancelDialogIsOpen_WhenAnAdminDismissesItInstead_ThenTheEventStaysLive() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Keep Live");
            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Keep it live" }).ClickAsync();

            await Expect(dialog).Not.ToBeVisibleAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" })).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Live Event whose Ends at has already elapsed, when an Admin views it, then it shows a PAST badge and leaves the default list")]
    public async Task GivenALiveEventWhoseEndsAtHasElapsed_WhenAnAdminViewsIt_ThenItShowsAPastBadgeAndLeavesTheDefaultList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Already Over");

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#StartsAt").FillAsync("2020-06-12T09:00");
            await Page.Locator("#EndsAt").FillAsync("2020-06-14T17:00");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard").ToString(),
                InteractiveUrl());

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Expect(Page.GetByText("PAST", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await GoToDashboardFilteredByNameAsync(name);
            await Expect(NoMatchMessage()).ToBeVisibleAsync(InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Live Event with dates set, when an Admin duplicates it, then the new Event pre-fills the dates, needs its own Name, and lands Draft with a fresh Slug and Passcode")]
    public async Task GivenALiveEventWithDatesSet_WhenAnAdminDuplicatesIt_ThenTheNewEventPreFillsTheDatesNeedsItsOwnNameAndLandsDraftWithAFreshSlugAndPasscode() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid sourceId, string sourceName) = await CreateEventAsync("Duplicate Source");

            await Page.GotoAsync(EventEditorUrl(sourceId));
            await Page.Locator("#StartsAt").FillAsync("2028-06-12T09:00");
            await Page.Locator("#EndsAt").FillAsync("2028-06-14T17:00");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard").ToString(),
                InteractiveUrl());

            await Page.GotoAsync(EventEditorUrl(sourceId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());
            string sourceSlug = await Page.Locator("#Slug").InputValueAsync();
            string sourcePasscode = await Page.Locator("#Passcode").InputValueAsync();

            await GoToDashboardFilteredByNameAsync(sourceName);
            ILocator sourceRow = EventRow(sourceName);
            await sourceRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Duplicate" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                new Regex(@"dashboard/events/new\?startsAt=.*&endsAt=.*"),
                InteractiveUrl());
            await Expect(Page.Locator("#Name")).ToHaveValueAsync(string.Empty);
            await Expect(Page.GetByText("Directors")).Not.ToBeVisibleAsync();
            await Expect(Page.Locator("#StartsAt")).ToHaveValueAsync("2028-06-12T09:00");
            await Expect(Page.Locator("#EndsAt")).ToHaveValueAsync("2028-06-14T17:00");

            string duplicateName = $"e2e-Duplicated {Guid.NewGuid():n}";
            await Page.Locator("#Name").FillAsync(duplicateName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create event" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                new Regex(@"dashboard/events/[0-9a-f-]{36}$"), new PageAssertionsToHaveURLOptions { Timeout = 20_000 });

            string path = new Uri(Page.Url).AbsolutePath;
            Guid duplicateId = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
            TrackEvent(duplicateId);

            Assert.NotEqual(sourceId, duplicateId);
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" })).ToBeVisibleAsync(
                InteractiveVisible());
            string duplicateSlug = await Page.Locator("#Slug").InputValueAsync();
            string duplicatePasscode = await Page.Locator("#Passcode").InputValueAsync();
            Assert.NotEqual(sourceSlug, duplicateSlug);
            Assert.NotEqual(sourcePasscode, duplicatePasscode);
        });

    [Fact(DisplayName = "Given a Director, when viewing the dashboard, then no duplicate action is available")]
    public async Task GivenADirector_WhenViewingTheDashboard_ThenNoDuplicateActionIsAvailable() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, string assignedName) = await CreateEventAsync("Director Viewable Duplicate");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await GoToDashboardFilteredByNameAsync(assignedName);
            ILocator row = EventRow(assignedName);
            await Expect(row).ToBeVisibleAsync(InteractiveVisible());
            await Expect(row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Duplicate" })).Not.ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a Live Event, when an Admin opens its guide from the dashboard, then the guide opens unlocked in a new tab")]
    public async Task GivenALiveEvent_WhenAnAdminOpensItsGuideFromTheDashboard_ThenTheGuideOpensUnlockedInANewTab() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Open Guide");
            string slug = await GoLiveAsync(eventId);

            await GoToDashboardFilteredByNameAsync(name);

            await ExpectOpenGuideToShowAsync(name, slug, "haven't been added yet");
        });

    [Fact(DisplayName = "Given a Past Event, when an Admin opens its guide from the dashboard, then the guide opens unlocked in a new tab")]
    public async Task GivenAPastEvent_WhenAnAdminOpensItsGuideFromTheDashboard_ThenTheGuideOpensUnlockedInANewTab() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Open Past Guide");
            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#StartsAt").FillAsync("2020-06-12T09:00");
            await Page.Locator("#EndsAt").FillAsync("2020-06-14T17:00");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString(), InteractiveUrl());
            string slug = await GoLiveAsync(eventId);

            await GoToDashboardFilteredByNameAsync(name);
            await FilterDashboardByStatusAsync("Past");

            await ExpectOpenGuideToShowAsync(name, slug, "haven't been added yet");
        });

    [Fact(DisplayName = "Given a Cancelled Event, when an Admin opens its guide from the dashboard, then the new tab shows the cancelled state")]
    public async Task GivenACancelledEvent_WhenAnAdminOpensItsGuideFromTheDashboard_ThenTheNewTabShowsTheCancelledState() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Open Cancelled Guide");
            string slug = await GoLiveAsync(eventId);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel event", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("CANCELLED", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                InteractiveVisible());

            await GoToDashboardFilteredByNameAsync(name);
            await FilterDashboardByStatusAsync("Cancelled");

            await ExpectOpenGuideToShowAsync(name, slug, "This event was cancelled");
        });

    [Fact(DisplayName = "Given a Director assigned to a Live Event, when they open its guide from the dashboard, then the guide opens unlocked in a new tab")]
    public async Task GivenADirectorAssignedToALiveEvent_WhenTheyOpenItsGuideFromTheDashboard_ThenTheGuideOpensUnlockedInANewTab() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string name) = await CreateEventAsync("Director Open Guide");
            string slug = await GoLiveAsync(eventId);
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(eventId);
            await GoToDashboardFilteredByNameAsync(name);

            await ExpectOpenGuideToShowAsync(name, slug, "haven't been added yet");
        });

    [Fact(DisplayName = "Given a Draft Event, when an Admin views the dashboard, then its Open guide button is disabled and says why on hover or focus")]
    public async Task GivenADraftEvent_WhenAnAdminViewsTheDashboard_ThenItsOpenGuideButtonIsDisabledAndSaysWhy() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Draft Guide");

            await GoToDashboardFilteredByNameAsync(name);
            ILocator row = EventRow(name);
            ILocator openGuide = row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open guide" });
            await Expect(openGuide).ToBeVisibleAsync(InteractiveVisible());

            await Expect(openGuide).ToBeDisabledAsync();
            await Expect(row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Open guide" }))
                .ToHaveCountAsync(0);

            ILocator wrapper = row.GetByRole(AriaRole.Group);
            ILocator tooltip = Page.Locator(".rz-tooltip");

            await wrapper.HoverAsync();
            await Expect(tooltip).ToContainTextAsync("Go live to open this guide.");

            await Page.Mouse.MoveAsync(0, 0);
            await Expect(tooltip).ToHaveCountAsync(0);

            await wrapper.FocusAsync();
            await Expect(tooltip).ToContainTextAsync("Go live to open this guide.");

            // Radzen rewrites attributes on its anchor as a tooltip opens and closes; the name has to survive that.
            await wrapper.BlurAsync();
            await Expect(tooltip).ToHaveCountAsync(0);
            await Expect(row.GetByRole(AriaRole.Group, new LocatorGetByRoleOptions { Name = "Draft guides can't be previewed yet" }))
                .ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin searches part of its name in a different case, then only it is listed until the search is cleared")]
    public async Task GivenAnExistingEvent_WhenAnAdminSearchesPartOfItsNameInADifferentCase_ThenOnlyItIsListedUntilTheSearchIsCleared() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Name Search");
            await CreateEventAsync("Name Search Other");

            string guidSuffix = name.Split(' ')[^1];
            await GoToDashboardFilteredByNameAsync(guidSuffix.ToUpperInvariant());

            await Expect(EventRow(name)).ToBeVisibleAsync(InteractiveVisible());
            await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(1, InteractiveCount());

            ILocator clear = ClearSearchButton();
            await clear.ClickAsync();

            await Expect(clear).ToHaveCountAsync(0, InteractiveCount());
            await Expect(NameSearchBox()).ToHaveValueAsync(string.Empty);
            await Expect(Page.Locator("tbody tr").Nth(1)).ToBeVisibleAsync(InteractiveVisible());
        });

    private ILocator ClearSearchButton() =>
        Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Clear search" });

    private ILocator NameSearchBox() =>
        Page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Search by name" });

    /// <remarks>
    /// By the Name cell's exact text, never a bare "has text" row match - the grid's no-match message quotes
    /// the search, so a loose match on an Event's name would be satisfied by the Event not being there.
    /// </remarks>
    private ILocator EventRow(string name) => Page.Locator("tbody tr")
        .Filter(new LocatorFilterOptions { Has = Page.GetByText(name, new PageGetByTextOptions { Exact = true }) });

    private ILocator NoMatchMessage() => Page.GetByText("No Events match");

    private async Task GoToDashboardFilteredByNameAsync(string text)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
        await FilterDashboardByNameAsync(text);
    }

    /// <remarks>
    /// The dashboard is prerendered, so text typed before its circuit connects is silently ignored; the clear
    /// button, which only an interactive component renders, is the proof the search landed, and the fill is
    /// repeated until it shows. Then waits for every row to contain the text - matching Events, or the no-match
    /// message that quotes it - so a caller never asserts against the unfiltered rows.
    /// </remarks>
    private async Task FilterDashboardByNameAsync(string text)
    {
        ILocator search = NameSearchBox();
        ILocator clear = ClearSearchButton();
        await Expect(search).ToBeVisibleAsync(InteractiveVisible());

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(InteractiveTimeoutMs);
        while (true)
        {
            await search.FillAsync(text);

            try
            {
                await Expect(clear).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 1_000 });
                break;
            }
            catch (PlaywrightException) when (DateTime.UtcNow < deadline)
            {
            }
        }

        await Expect(Page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasNotText = text }))
            .ToHaveCountAsync(0, InteractiveCount());
    }

    private async Task<string> GoLiveAsync(Guid eventId)
    {
        await Page.GotoAsync(EventEditorUrl(eventId));
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }))
            .ToBeVisibleAsync(InteractiveVisible());
        return await Page.Locator("#Slug").InputValueAsync();
    }

    private async Task FilterDashboardByStatusAsync(string status)
    {
        ILocator statusFilterDropdown = Page.Locator(".rz-dropdown");
        await Expect(statusFilterDropdown).ToBeVisibleAsync(InteractiveVisible());
        await statusFilterDropdown.ClickAsync();
        ILocator option = Page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = status });
        await Expect(option).ToBeVisibleAsync(InteractiveVisible());
        await option.ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
    }

    private async Task ExpectOpenGuideToShowAsync(string eventName, string slug, string guideText)
    {
        ILocator row = EventRow(eventName);
        ILocator openGuide = row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Open guide" });
        await Expect(openGuide).ToBeVisibleAsync(InteractiveVisible());
        await Expect(openGuide).ToHaveAttributeAsync("href", $"e/{slug}");
        await Expect(openGuide).ToHaveAttributeAsync("target", "_blank");

        IPage guide = await Page.Context.RunAndWaitForPageAsync(() => openGuide.ClickAsync());

        await Expect(guide).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());
        await Expect(guide.GetByText(guideText)).ToBeVisibleAsync(InteractiveVisible());
    }

    /// <remarks>
    /// Scoped to the field's own container, not a bare page-wide <c>GetByText</c> - a collision on
    /// <c>Name</c> also collides <c>Slug</c> when it's left blank (Slug then derives from Name), which
    /// renders its own "already in use" message, and both the per-field <c>ValidationMessage</c> and the
    /// form's <c>ValidationSummary</c> render each collision once more - so an unscoped text match resolves
    /// to multiple elements (a Playwright strict-mode violation) rather than the one field under test.
    /// </remarks>
    private ILocator FieldErrorLocator(string fieldId) =>
        Page.Locator(".vlg-field").Filter(new LocatorFilterOptions { Has = Page.Locator($"#{fieldId}") });

    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        IdentityUserDto director = await CreateTrackedUserAsync("e2e-director", CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, director.Email!, TestCredentials.KnownPassword);
        return director;
    }
}
