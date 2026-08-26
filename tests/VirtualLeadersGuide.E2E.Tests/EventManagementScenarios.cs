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
            string name = $"Summer Camporee {Guid.NewGuid():n}";

            await CreateEventAsync(name);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await AssertEventVisibleInGridAsync(name);
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin edits its name, address, and passcode, then the changes persist")]
    public async Task GivenAnExistingEvent_WhenAnAdminEditsItsNameAddressAndPasscode_ThenTheChangesPersist() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            Guid eventId = await CreateEventAsync($"Fall Webelos Woods {Guid.NewGuid():n}");

            string renamedTo = $"Renamed {Guid.NewGuid():n}";
            string newSlug = $"renamed-{Guid.NewGuid():n}";
            string newPasscode = $"Testcode{Guid.NewGuid():n}";

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#Name").FillAsync(renamedTo);
            await Page.Locator("#Slug").FillAsync(newSlug);
            await Page.Locator("#Passcode").FillAsync(newPasscode);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();

            // A successful save navigates back to the list (EventEditor.razor's UpdateAsync) - wait for
            // that URL change, not a bare ClickAsync, which only waits for the DOM click event to dispatch,
            // not for the save's own round trip to actually complete server-side.
            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard").ToString(),
                new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Expect(Page.Locator("#Name")).ToHaveValueAsync(renamedTo);
            await Expect(Page.Locator("#Slug")).ToHaveValueAsync(newSlug);
            await Expect(Page.Locator("#Passcode")).ToHaveValueAsync(newPasscode);
        });

    [Fact(DisplayName = "Given an existing Event, when an Admin creates another with the same name, then the error lands on the name field")]
    public async Task GivenAnExistingEvent_WhenAnAdminCreatesAnotherWithTheSameName_ThenTheErrorLandsOnTheNameField() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string name = $"Winter Klondike {Guid.NewGuid():n}";
            await CreateEventAsync(name);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());
            await Page.Locator("#Name").FillAsync(name);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create event" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());

            // Scoped to the Name field's own container, not a bare GetByText - leaving Slug blank makes it
            // derive from Name, so it collides too and renders its own "already in use" message (both the
            // per-field ValidationMessage and the form's ValidationSummary render each collision once more),
            // and a bare text match resolves to multiple elements (Playwright strict-mode violation).
            ILocator nameField = Page.Locator(".vlg-field").Filter(new LocatorFilterOptions { Has = Page.Locator("#Name") });
            await Expect(nameField.GetByText("already in use by another Event")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given a Director assigned to one Event, when viewing the dashboard, then only that Event is listed read-only with no create button")]
    public async Task GivenADirectorAssignedToOneEvent_WhenViewingTheDashboard_ThenOnlyThatEventIsListedReadOnlyWithNoCreateButton() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string assignedName = $"Spring Klondike {Guid.NewGuid():n}";
            string unassignedName = $"Not Assigned {Guid.NewGuid():n}";
            Guid assignedEventId = await CreateEventAsync(assignedName);
            await CreateEventAsync(unassignedName);
            await SignOutAsync();

            IdentityUserDto director = await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My events", Exact = true }))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            // The grid's own LoadData round trip is what actually proves the scoping - wait for its content,
            // not just the static heading above it.
            await Expect(Page.GetByText(assignedName)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(unassignedName)).Not.ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ New event" })).Not.ToBeVisibleAsync();

            await Page.GotoAsync(EventEditorUrl(assignedEventId));
            await Expect(Page.GetByText("VIEW ONLY")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.Locator("#Name")).Not.ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" })).Not.ToBeVisibleAsync();

            _ = director;
        });

    [Fact(DisplayName = "Given a Director, when navigating directly to an Event they aren't assigned to, then they are denied")]
    public async Task GivenADirector_WhenNavigatingDirectlyToAnEventTheyArentAssignedTo_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            Guid assignedEventId = await CreateEventAsync($"Assigned {Guid.NewGuid():n}");
            Guid unassignedEventId = await CreateEventAsync($"Unassigned {Guid.NewGuid():n}");
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
            Guid assignedEventId = await CreateEventAsync($"Assigned {Guid.NewGuid():n}");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.Locator("#Name")).Not.ToBeVisibleAsync();
        });

    /// <remarks>
    /// <see cref="AspireE2EFixture.AdminAllowlistedEmail"/> is one value shared by every test in this run
    /// (ADR-0025 - one Aspire stack, one fixture instance) - unlike every other account this class creates,
    /// which is freshly guid-suffixed per call. Every test in this class needs an Admin, so this checks
    /// before creating rather than assuming a fresh account the way <see cref="IdentityApiClient"/>'s own
    /// header remarks otherwise call for.
    /// </remarks>
    private async Task SignInAsAdminAsync()
    {
        if (!await Fixture.IdentityApi.ExistsAsync(Fixture.AdminAllowlistedEmail, CancellationToken.None))
        {
            await Fixture.IdentityApi.CreateUserAsync(
                Fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword, CancellationToken.None);
        }

        await new LoginPage(Page).SignInAsync(
            Fixture.WebBaseUrl, Fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword);
    }

    /// <remarks>
    /// The Admin grid is never scoped (unlike a Director's) - it lists every Event this local dev machine's
    /// persistent SQL volume has ever accumulated across every past run of this suite
    /// (<see cref="AspireE2EFixture"/>'s own remarks: the data volume outlives any one run), paged 10 at a
    /// time with no search box (out of scope for P2-9, #18). A freshly created Event can land on any page
    /// depending on how many prior runs' Events already exist, so asserting against the grid's default
    /// first page - as opposed to a Director's grid, which only ever shows what that one test itself
    /// assigned - is not reliable. This pages forward through the grid's own pagination controls looking
    /// for the target text, rather than assuming page 1.
    /// </remarks>
    private async Task AssertEventVisibleInGridAsync(string name)
    {
        const int maxPages = 50;

        for (int page = 1; page <= maxPages; page++)
        {
            if (await Page.GetByText(name).CountAsync() > 0)
            {
                return;
            }

            ILocator nextPageButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Go to next page." });
            if (!await nextPageButton.IsEnabledAsync())
            {
                break;
            }

            await nextPageButton.ClickAsync();
        }

        // One last wait on the current (possibly still-loading, possibly final) page before failing -
        // covers both "the grid's LoadData for this page hasn't settled yet" and "it's genuinely missing."
        await Expect(Page.GetByText(name)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
    }

    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        string email = $"e2e-director-{Guid.NewGuid():n}@example.test";
        IdentityUserDto director =
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);
        return director;
    }

    /// <remarks>
    /// The only sign-out affordance is the header's real <c>POST Account/Logout</c> form
    /// (<c>SignOutForm.razor</c>) - clicking it, rather than clearing cookies directly, exercises the same
    /// path a real session change goes through and keeps this test from assuming cookie storage details.
    /// </remarks>
    private async Task SignOutAsync()
    {
        await Page.GotoAsync(Fixture.WebBaseUrl.ToString());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
    }

    /// <summary>Creates an Event via the real UI and returns its id, parsed off the resulting edit URL.</summary>
    /// <remarks>
    /// The URL-change assertion below carries a longer-than-default timeout - <c>/dashboard/events/new</c>
    /// is an <c>InteractiveServer</c> page (ADR-0034), so a click has to survive a real SignalR round trip,
    /// not just a DOM update; the first such round trip in a fresh browser context (cold circuit connect)
    /// can comfortably exceed Playwright's 5s assertion default on a loaded machine.
    /// </remarks>
    private async Task<Guid> CreateEventAsync(string name)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());
        await Page.Locator("#Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create event" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Regex(@"dashboard/events/[0-9a-f-]{36}$"), new PageAssertionsToHaveURLOptions { Timeout = 20_000 });

        string path = new Uri(Page.Url).AbsolutePath;
        return Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    private string EventEditorUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}").ToString();
}
