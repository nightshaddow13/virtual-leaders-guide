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
[Collection(nameof(AspireE2ECollection))]
public class EventManagementScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>
    /// Every page in this class is <c>InteractiveServer</c> (ADR-0034), so an assertion that depends on a
    /// SignalR round trip - a click's server-side handler, a <c>RadzenDataGrid</c>'s <c>LoadData</c> after
    /// navigating in - needs more room than Playwright's 5s assertion default, especially on a fresh
    /// per-test browser context's first interactive page (cold circuit connect).
    /// </remarks>
    private const int InteractiveTimeoutMs = 15_000;

    [Fact(DisplayName = "Given a signed-in Admin, when creating an Event with only a name, then it appears in the Events list")]
    public async Task GivenASignedInAdmin_WhenCreatingAnEventWithOnlyAName_ThenItAppearsInTheEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Summer Camporee");

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await Expect(Page.GetByText(name)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

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
                new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await Expect(row.GetByText("JUN 12")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin deletes it and confirms, then it is removed from the Events list")]
    public async Task GivenAnExistingEvent_WhenAnAdminDeletesItAndConfirms_ThenItIsRemovedFromTheEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("To Delete");

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();

            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(row).Not.ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given the delete dialog is open, when an Admin cancels instead, then the Event remains listed")]
    public async Task GivenTheDeleteDialogIsOpen_WhenAnAdminCancelsInstead_ThenTheEventRemainsListed() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (_, string name) = await CreateEventAsync("Not Deleted");

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();

            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();

            await Expect(dialog).Not.ToBeVisibleAsync();
            await Expect(Page.GetByText(name)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My events", Exact = true }))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(assignedName)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(unassignedName)).Not.ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ New event" })).Not.ToBeVisibleAsync();

            await Page.GotoAsync(EventEditorUrl(assignedEventId));
            await Expect(Page.GetByText("VIEW ONLY")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = assignedName });
            await Expect(row).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" })).Not.ToBeVisibleAsync();

            await Page.GotoAsync(EventEditorUrl(assignedEventId));
            await Expect(Page.GetByText("VIEW ONLY")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await Expect(row.GetByText("LIVE", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(dialog.GetByText($"Cancel {name}?")).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel event", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("CANCELLED", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await Expect(Page.GetByText(name)).Not.ToBeVisibleAsync();

            ILocator statusFilterDropdown = Page.Locator(".rz-dropdown");
            await Expect(statusFilterDropdown).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await statusFilterDropdown.ClickAsync();
            ILocator cancelledOption = Page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Cancelled" });
            await Expect(cancelledOption).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await cancelledOption.ClickAsync();
            await Page.Keyboard.PressAsync("Escape");

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await Expect(row).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel event" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Keep it live" }).ClickAsync();

            await Expect(dialog).Not.ToBeVisibleAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
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
                new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go live" }).ClickAsync();
            await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Expect(Page.GetByText("PAST", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await Expect(Page.GetByText(name)).Not.ToBeVisibleAsync();
        });

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
