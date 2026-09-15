using System.Text.RegularExpressions;
using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P5-17 (#22): dashboard authoring of an Event's InfoPages. Every InfoPage here is created through
/// the real UI within the same scenario, not seeded via an API helper (grilled decision - no
/// <c>InfoPagesApiClient</c> this ticket) - InfoPages cascade-delete with their Event (<c>PageSchemaShould</c>),
/// so <see cref="E2ETestBase.TrackEvent"/> alone covers cleanup (ADR-0039). Mirrors
/// <see cref="EventManagementScenarios"/>'s shape - <see cref="InteractiveTimeoutMs"/>,
/// <see cref="CreateAndSignInDirectorAsync"/>, and the dialog/field-error locator conventions are the same,
/// kept as this class's own copy rather than promoted to the shared base, matching that class's own
/// precedent for why (each scenario class keeps what it needs, rather than growing a shared surface no
/// other class asked for).
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class InfoPageManagementScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>See <see cref="EventManagementScenarios.InteractiveTimeoutMs"/>'s identical remarks - every page here is <c>InteractiveServer</c> too.</remarks>
    private const int InteractiveTimeoutMs = 15_000;

    [Fact(DisplayName = "Given an Event, when an Admin creates an InfoPage with markdown content, then it renders sanitized in Preview and appears in the list after saving")]
    public async Task GivenAnEvent_WhenAnAdminCreatesAnInfoPageWithMarkdownContent_ThenItRendersSanitizedInPreviewAndAppearsInTheListAfterSaving() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Info Page Host");
            string title = $"e2e-Packing List {Guid.NewGuid():n}";

            await Page.GotoAsync(NewInfoPageUrl(eventId));
            await Page.Locator("#Title").FillAsync(title);
            await Page.Locator("#MarkdownContent").FillAsync("Bring a **jacket** and <script>alert(1)</script>.");

            await Expect(Page.Locator(".ip-pane-preview strong")).ToHaveTextAsync("jacket");
            await Expect(Page.Locator(".ip-pane-preview")).Not.ToContainTextAsync("<script>");

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create page" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                new Regex(@"info-pages/[0-9a-f-]{36}$"), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(InfoPageListUrl(eventId));
            await Expect(Page.GetByText(title)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an existing InfoPage, when an Admin edits its title and deletes it, then the change persists and the deletion removes it from the list")]
    public async Task GivenAnExistingInfoPage_WhenAnAdminEditsItsTitleAndDeletesIt_ThenTheChangePersistsAndTheDeletionRemovesItFromTheList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Info Page Edit Host");
            Guid infoPageId = await CreateInfoPageAsync(eventId, "Original Title");

            string renamedTo = $"e2e-Renamed {Guid.NewGuid():n}";
            await Page.GotoAsync(InfoPageEditorUrl(eventId, infoPageId));
            await Page.Locator("#Title").FillAsync(renamedTo);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                InfoPageListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(renamedTo)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = renamedTo });
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(row).Not.ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    /// <remarks>Pins ADR-0059: an assigned Director gets the exact same full CRUD an Admin does - unlike Event's own details, which stay Admin-only.</remarks>
    [Fact(DisplayName = "Given a Director assigned to an Event, when they create, edit, and delete an InfoPage, then every action succeeds the same way it would for an Admin")]
    public async Task GivenADirectorAssignedToAnEvent_WhenTheyCreateEditAndDeleteAnInfoPage_ThenEveryActionSucceedsTheSameWayItWouldForAnAdmin() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Director InfoPage Host");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(eventId);

            string title = $"e2e-Director Page {Guid.NewGuid():n}";
            await Page.GotoAsync(NewInfoPageUrl(eventId));
            await Page.Locator("#Title").FillAsync(title);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create page" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                new Regex(@"info-pages/[0-9a-f-]{36}$"), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            string renamedTo = $"e2e-Director Renamed {Guid.NewGuid():n}";
            await Page.Locator("#Title").FillAsync(renamedTo);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                InfoPageListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = renamedTo });
            await Expect(row).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(row).Not.ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given a Director not assigned to an Event, when navigating directly to its Info pages, then they are denied")]
    public async Task GivenADirectorNotAssignedToAnEvent_WhenNavigatingDirectlyToItsInfoPages_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned For InfoPages");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned For InfoPages");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(InfoPageListUrl(unassignedEventId));

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    private string InfoPageListUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages").ToString();

    private string NewInfoPageUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages/new").ToString();

    private string InfoPageEditorUrl(Guid eventId, Guid infoPageId) =>
        new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/info-pages/{infoPageId}").ToString();

    /// <summary>Creates an InfoPage through the real UI, returning its id. See <see cref="E2ETestBase.CreateEventAsync"/>'s identical shape.</summary>
    private async Task<Guid> CreateInfoPageAsync(Guid eventId, string label)
    {
        string title = $"e2e-{label} {Guid.NewGuid():n}";

        await Page.GotoAsync(NewInfoPageUrl(eventId));
        await Page.Locator("#Title").FillAsync(title);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create page" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Regex(@"info-pages/[0-9a-f-]{36}$"), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

        string path = new Uri(Page.Url).AbsolutePath;
        return Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    /// <remarks>Kept local, not shared - see this class's own header remarks.</remarks>
    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        IdentityUserDto director = await CreateTrackedUserAsync("e2e-director", CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, director.Email!, TestCredentials.KnownPassword);
        return director;
    }
}
