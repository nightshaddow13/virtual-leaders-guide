using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Covers P4-2 (#72): the public entry path at <c>/</c> and <c>/e/{slug}</c> - event lookup, passcode entry,
/// and the landing states around it (wrong passcode, unknown address, Cancelled/Draft, the signed-in staff
/// bypass, and a Passcode rotation revoking an existing Unlock).
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
[Collection(nameof(AspireE2ECollection))]
public class PublicGuideScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given a Live Event's address and correct passcode, when a visitor submits them on /, then they reach the unlocked guide")]
    public async Task GivenALiveEventsAddressAndCorrectPasscode_WhenAVisitorSubmitsThemOnHome_ThenTheyReachTheUnlockedGuide() =>
        await RunAsync(async () =>
        {
            (string slug, string passcode) = await CreateAndPublishEventAsync("Summer Camporee");
            await SignOutAsync();

            await SubmitOnHomeAsync(slug, passcode);

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());
            await Expect(Page.GetByText("This guide is unlocked")).ToBeVisibleAsync();
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
            await Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel event", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("CANCELLED", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
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

            await Expect(Page.GetByText("This guide is unlocked")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a visitor who already unlocked an Event's guide, when an Admin then rotates its Passcode, then the visitor is locked again on their next visit")]
    public async Task GivenAVisitorWhoAlreadyUnlockedAGuide_WhenAnAdminRotatesThePasscode_ThenTheVisitorIsLockedAgainOnTheirNextVisit() =>
        await RunAsync(async () =>
        {
            (Guid eventId, string slug, string passcode) = await CreateAndPublishEventReturningIdAsync("Rotate Me");
            await SignOutAsync();
            await SubmitOnHomeAsync(slug, passcode);
            await Expect(Page.GetByText("This guide is unlocked")).ToBeVisibleAsync();

            await SignInAsAdminAsync();
            string newPasscode = $"Testcode{Guid.NewGuid():n}";
            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.Locator("#Passcode").FillAsync(newPasscode);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await SignOutAsync();

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, $"e/{slug}").ToString());

            await Expect(Page.GetByText("Unlock this guide")).ToBeVisibleAsync();
        });

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
        await Expect(Page.GetByText("LIVE", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();

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
