using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P5-6 (#87): dashboard creation of an Event's Activities. Create-only, mirroring
/// <see cref="InfoPageManagementScenarios"/>'s create scenario shape - there is no Activities list or edit
/// page yet (P5-7/#93, P5-8/#94), so a successful create lands back on the Event page rather than a
/// per-Activity URL. Every Activity here is created through the real UI within the same scenario -
/// Activities cascade-delete with their Event, so <see cref="E2ETestBase.TrackEvent"/> alone covers cleanup
/// (ADR-0039). The sanitized-preview scenario below asserts on live <c>&lt;script&gt;</c> element count, not
/// rendered text content, because the sanitizer escapes raw HTML to inert visible text rather than dropping
/// it (confirmed by <c>MarkdownRendererShould</c>) - the escaped text legitimately still contains the
/// substring <c>"&lt;script&gt;"</c>, so a text-content assertion would pass even if sanitizing broke.
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

            await Expect(Page.Locator(".ae-pane-preview strong")).ToHaveTextAsync("jacket");
            await Expect(Page.Locator(".ae-pane-preview script")).ToHaveCountAsync(0);

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create activity" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                EventUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
        });

    /// <remarks>Pins ADR-0069: an assigned Director gets the exact same full CRUD an Admin does - unlike Event's own details, which stay Admin-only.</remarks>
    [Fact(DisplayName = "Given a Director assigned to an Event, when they create an Activity, then it succeeds the same way it would for an Admin")]
    public async Task GivenADirectorAssignedToAnEvent_WhenTheyCreateAnActivity_ThenItSucceedsTheSameWayItWouldForAnAdmin() =>
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
                EventUrl(eventId), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
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

    private string EventUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}").ToString();

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
