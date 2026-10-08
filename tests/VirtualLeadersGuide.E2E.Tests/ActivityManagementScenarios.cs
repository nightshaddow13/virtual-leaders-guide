using System.Text.RegularExpressions;
using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P5-6 (#87, create), P5-7 (#93, list) and P5-8 (#94, edit). A successful create lands on the new
/// Activity's own edit page, so create scenarios assert a detail-URL pattern and then navigate to the list
/// themselves when they need to see it. Every
/// Activity here is created through the real UI within the same scenario - Activities cascade-delete with
/// their Event, so <see cref="E2ETestBase.TrackEvent"/> alone covers cleanup (ADR-0039). The
/// sanitized-preview scenario below asserts on live <c>&lt;script&gt;</c> element count, not rendered text
/// content, because the sanitizer escapes raw HTML to inert visible text rather than dropping it (confirmed
/// by <c>MarkdownRendererShould</c>) - the escaped text legitimately still contains the substring
/// <c>"&lt;script&gt;"</c>, so a text-content assertion would pass even if sanitizing broke. The
/// list-after-create scenario below deliberately re-navigates to the Event page and clicks its own
/// "Activities" button rather than trusting the create redirect already landed on the list - that's what
/// actually proves the new <c>EventEditor</c> entry point works, not just that create-then-redirect does.
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class ActivityManagementScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>See <see cref="EventManagementScenarios.InteractiveTimeoutMs"/>'s identical remarks - every page here is <c>InteractiveServer</c> too.</remarks>
    private const int InteractiveTimeoutMs = 15_000;

    [Fact(DisplayName = "Given an Event, when an Admin creates an Activity with markdown Description, then it renders sanitized in Preview and the create succeeds")]
    public async Task GivenAnEvent_WhenAnAdminCreatesAnActivityWithMarkdownDescription_ThenItRendersSanitizedInPreviewAndTheCreateSucceeds() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Activity Host");
            string name = $"e2e-Canoe Basics {Guid.NewGuid():n}";

            await Page.GotoAsync(NewActivityUrl(eventId));
            await Page.Locator("#Name").FillAsync(name);
            await Page.Locator("#Description").FillAsync("Bring a **jacket** and <script>alert(1)</script>.");

            await Expect(Page.Locator(".md-pane-preview strong")).ToHaveTextAsync("jacket");
            await Expect(Page.Locator(".md-pane-preview script")).ToHaveCountAsync(0);

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                ActivityDetailUrlPattern, new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(ActivitiesListUrl(eventId));
            await Expect(Page.GetByText(name)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an existing Activity, when an Admin edits its Name and Description, then the change persists")]
    public async Task GivenAnExistingActivity_WhenAnAdminEditsItsNameAndDescription_ThenTheChangePersists() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Activity Edit Host");
            Guid activityId = await CreateActivityAsync(eventId, "Original Name");

            string renamedTo = $"e2e-Renamed {Guid.NewGuid():n}";
            await Page.GotoAsync(ActivityEditorUrl(eventId, activityId));
            await Page.Locator("#Name").FillAsync(renamedTo);
            await Page.Locator("#Description").FillAsync("Now with **portaging**.");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                ActivitiesListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(renamedTo)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(ActivityEditorUrl(eventId, activityId));
            await Expect(Page.Locator("#Name")).ToHaveValueAsync(
                renamedTo, new LocatorAssertionsToHaveValueOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.Locator(".md-pane-preview strong")).ToHaveTextAsync("portaging");
        });

    /// <remarks>Pins ADR-0069: an assigned Director gets the exact same full CRUD an Admin does - unlike Event's own details, which stay Admin-only.</remarks>
    [Fact(DisplayName = "Given a Director assigned to an Event, when they create and edit an Activity, then both succeed the same way they would for an Admin")]
    public async Task GivenADirectorAssignedToAnEvent_WhenTheyCreateAndEditAnActivity_ThenBothSucceedTheSameWayTheyWouldForAnAdmin() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Director Activity Host");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(eventId);

            string name = $"e2e-Director Activity {Guid.NewGuid():n}";
            await Page.GotoAsync(NewActivityUrl(eventId));
            await Page.Locator("#Name").FillAsync(name);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                ActivityDetailUrlPattern, new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            string renamedTo = $"e2e-Director Renamed {Guid.NewGuid():n}";
            await Page.Locator("#Name").FillAsync(renamedTo);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                ActivitiesListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(renamedTo)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an Event with Activities, when an Admin opens its Activities list, then each Activity appears")]
    public async Task GivenAnEventWithActivities_WhenAnAdminOpensItsActivitiesList_ThenEachActivityAppears() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Activities List Host");
            string name = $"e2e-Archery Range {Guid.NewGuid():n}";

            await Page.GotoAsync(NewActivityUrl(eventId));
            await Page.Locator("#Name").FillAsync(name);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                ActivityDetailUrlPattern, new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(EventUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Activities" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                ActivitiesListUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText(name)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given a Director not assigned to an Event, when navigating directly to its Activities list, then they are denied")]
    public async Task GivenADirectorNotAssignedToAnEvent_WhenNavigatingDirectlyToItsActivities_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned For Activities List");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned For Activities List");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(ActivitiesListUrl(unassignedEventId));

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given a Director not assigned to an Event, when navigating directly to its new-Activity form, then they are denied")]
    public async Task GivenADirectorNotAssignedToAnEvent_WhenNavigatingDirectlyToItsNewActivityForm_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned For Activities");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned For Activities");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(NewActivityUrl(unassignedEventId));

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given a Director not assigned to an Event, when navigating directly to one of its Activities' editor, then they are denied")]
    public async Task GivenADirectorNotAssignedToAnEvent_WhenNavigatingDirectlyToOneOfItsActivitiesEditor_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid assignedEventId, _) = await CreateEventAsync("Assigned For Activity Editor");
            (Guid unassignedEventId, _) = await CreateEventAsync("Unassigned For Activity Editor");
            Guid activityId = await CreateActivityAsync(unassignedEventId, "Hidden Activity");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(assignedEventId);

            await Page.GotoAsync(ActivityEditorUrl(unassignedEventId, activityId));

            await Expect(Page.GetByText("You don't have access to this Event")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    private static Regex ActivityDetailUrlPattern { get; } = new(@"activities/[0-9a-f-]{36}$");

    private string ActivityEditorUrl(Guid eventId, Guid activityId) =>
        new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/{activityId}").ToString();

    /// <summary>Creates an Activity through the real UI, returning its id. See <see cref="E2ETestBase.CreateEventAsync"/>'s identical shape.</summary>
    private async Task<Guid> CreateActivityAsync(Guid eventId, string label)
    {
        string name = $"e2e-{label} {Guid.NewGuid():n}";

        await Page.GotoAsync(NewActivityUrl(eventId));
        await Page.Locator("#Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            ActivityDetailUrlPattern, new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

        string path = new Uri(Page.Url).AbsolutePath;
        return Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    private string EventUrl(Guid eventId) =>new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}").ToString();

    private string ActivitiesListUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities").ToString();

    private string NewActivityUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}/activities/new").ToString();

    /// <remarks>Kept local, not shared - see <see cref="InfoPageManagementScenarios"/>'s identical copy and its own header remarks.</remarks>
    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        IdentityUserDto director = await CreateTrackedUserAsync("e2e-director", CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, director.Email!, TestCredentials.KnownPassword);
        return director;
    }
}
