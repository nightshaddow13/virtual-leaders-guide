using System.Text.RegularExpressions;
using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P2-12 (#43): inviting a Director by email from <c>/dashboard/users</c>, completing account setup
/// at <c>/setup</c>, assigning the invited Director to an Event from the Event's own Directors section
/// (ADR-0035 - never the reverse), and the resend/revoke actions on a pending invite.
/// <see cref="AspireE2EFixture.EmailSink"/> intercepts the invite email the same way
/// <see cref="PasswordResetScenarios"/> intercepts a reset link. <c>IdentityApiClient.GrantDirectorAsync</c>
/// (added for P2-9, #18) is deliberately unused here - the point of these scenarios is that the invite UI
/// itself creates the Role/Grant rows, not a seeding shortcut.
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class DirectorInviteScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>Same reasoning as <see cref="EventManagementScenarios"/>'s own constant - every page here is <c>InteractiveServer</c> (ADR-0036).</remarks>
    private const int InteractiveTimeoutMs = 15_000;

    [Fact(DisplayName = "Given a signed-in Admin, when inviting a new email as a Director, then the invite email arrives and the person shows as Invited")]
    public async Task GivenASignedInAdmin_WhenInvitingANewEmailAsADirector_ThenTheInviteEmailArrivesAndThePersonShowsAsInvited() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-{Guid.NewGuid():n}@example.test";

            await OpenInviteDialogAndSendAsync(email, displayName: "Dana Okafor");

            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);
            Assert.Equal(SentEmailKinds.DirectorInvite, inviteEmail.Kind);
            Assert.Contains("/setup?", inviteEmail.Payload, StringComparison.Ordinal);

            await SearchUsersAsync(email);
            await Expect(Page.GetByText(email)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText("Invited")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given an invited Director, when they complete account setup, then they can sign in and see an empty Events list")]
    public async Task GivenAnInvitedDirector_WhenTheyCompleteAccountSetup_ThenTheyCanSignInAndSeeAnEmptyEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-setup-{Guid.NewGuid():n}@example.test";
            await OpenInviteDialogAndSendAsync(email, displayName: null);

            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);
            await SignOutAsync();

            await Page.GotoAsync(inviteEmail.Payload);
            await SubmitSetupAsync(TestCredentials.KnownPassword);
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/Login").ToString());

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My events", Exact = true }))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByText("No Events are assigned to you yet.")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an activated Director, when an Admin assigns them to an Event from the Event's page, then it appears on their dashboard after signing in again")]
    public async Task GivenAnActivatedDirector_WhenAnAdminAssignsThemToAnEventFromTheEventsPage_ThenItAppearsOnTheirDashboardAfterSigningInAgain() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-assign-{Guid.NewGuid():n}@example.test";
            string eventName = $"Spring Kickoff {Guid.NewGuid():n}";

            Guid eventId = await CreateEventAsync(eventName);
            await OpenInviteDialogAndSendAsync(email, displayName: null);
            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);

            await SignOutAsync();
            await Page.GotoAsync(inviteEmail.Payload);
            await SubmitSetupAsync(TestCredentials.KnownPassword);

            await SignInAsAdminAsync();
            await Page.GotoAsync(EventEditorUrl(eventId));
            await AddDirectorToEventAsync(email);
            await Expect(Page.GetByText(email)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await SignOutAsync();
            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

            await Expect(Page.GetByText(eventName)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an email that already belongs to a User, when an Admin tries to invite it again, then the existing account is shown and no email is sent")]
    public async Task GivenAnEmailThatAlreadyBelongsToAUser_WhenAnAdminTriesToInviteItAgain_ThenTheExistingAccountIsShownAndNoEmailIsSent() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-duplicate-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await OpenInviteDialogAsync();
            await Page.Locator("#Email").FillAsync(email);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();

            await Expect(Page.GetByText("already exists")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open this user" }))
                .ToBeVisibleAsync();

            await EstablishNoEmailWasWrittenHappensBeforeAsync();
            Assert.False(Fixture.EmailSink.HasEmailFor(email));
        });

    [Fact(DisplayName = "Given a pending invite, when an Admin resends it, then a second email arrives and the first link no longer works")]
    public async Task GivenAPendingInvite_WhenAnAdminResendsIt_ThenASecondEmailArrivesAndTheFirstLinkNoLongerWorks() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-resend-{Guid.NewGuid():n}@example.test";
            await OpenInviteDialogAndSendAsync(email, displayName: null);
            SentEmailDto firstInvite = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);

            await SearchUsersAsync(email);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Resend email" }).ClickAsync();

            SentEmailDto secondInvite = await WaitForASecondEmailAsync(email, firstInvite);

            await SignOutAsync();
            await Page.GotoAsync(firstInvite.Payload);
            await Expect(Page.GetByText("Invalid invite")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await Page.GotoAsync(secondInvite.Payload);
            await SubmitSetupAsync(TestCredentials.KnownPassword);
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/Login").ToString());
        });

    [Fact(DisplayName = "Given a pending invite, when an Admin revokes it, then the person disappears from the Users screen and their setup link is invalid")]
    public async Task GivenAPendingInvite_WhenAnAdminRevokesIt_ThenThePersonDisappearsFromTheUsersScreenAndTheirSetupLinkIsInvalid() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-revoke-{Guid.NewGuid():n}@example.test";
            await OpenInviteDialogAndSendAsync(email, displayName: null);
            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);

            await SearchUsersAsync(email);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Revoke invite" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard/users").ToString(),
                new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
            await SearchUsersAsync(email);
            await Expect(Page.GetByText(email)).Not.ToBeVisibleAsync();

            await SignOutAsync();
            await Page.GotoAsync(inviteEmail.Payload);
            await Expect(Page.GetByText("Invalid invite")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    /// <remarks>
    /// Same happens-before reasoning as <see cref="PasswordResetScenarios.EstablishNoEmailWasWrittenHappensBeforeAsync"/>:
    /// proving a negative needs a known email to land afterward, not a sleep.
    /// </remarks>
    private async Task EstablishNoEmailWasWrittenHappensBeforeAsync()
    {
        string barrierEmail = $"e2e-invite-barrier-{Guid.NewGuid():n}@example.test";
        await OpenInviteDialogAndSendAsync(barrierEmail, displayName: null);
        await Fixture.EmailSink.WaitForEmailAsync(barrierEmail, CancellationToken.None);
    }

    private async Task<SentEmailDto> WaitForASecondEmailAsync(string email, SentEmailDto firstInvite)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            SentEmailDto candidate = await Fixture.EmailSink.WaitForEmailAsync(email, deadline.Token);
            if (candidate.SentAtUtc > firstInvite.SentAtUtc)
            {
                return candidate;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
        }
    }

    private async Task OpenInviteDialogAsync()
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/users").ToString());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Invite director" }).ClickAsync();
        await Expect(Page.Locator("#Email")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
    }

    private async Task OpenInviteDialogAndSendAsync(string email, string? displayName)
    {
        await OpenInviteDialogAsync();
        await Page.Locator("#Email").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send invitation" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        if (displayName is not null)
        {
            await Page.Locator("#DisplayName").FillAsync(displayName);
        }

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send invitation" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Done" }).ClickAsync(
            new LocatorClickOptions { Timeout = InteractiveTimeoutMs });
    }

    private async Task SearchUsersAsync(string search)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/users").ToString());
        await Page.GetByPlaceholder("Search email or name").FillAsync(search);
        await Expect(Page.GetByText(search)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
    }

    private async Task SubmitSetupAsync(string password)
    {
        await Page.Locator("#Input\\.Password").FillAsync(password);
        await Page.Locator("#Input\\.ConfirmPassword").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Activate account" }).ClickAsync();
    }

    /// <remarks>
    /// Radzen's dropdown renders its option list in a popup keyed by visible text - <c>EventEditor.razor</c>'s
    /// picker uses each candidate's email as <c>TextProperty</c> (ADR-0035), so the option's accessible name
    /// is the email itself.
    /// </remarks>
    private async Task AddDirectorToEventAsync(string directorEmail)
    {
        await Page.Locator(".rz-dropdown").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = directorEmail }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add" }).ClickAsync();
    }

    private async Task SignOutAsync()
    {
        await Page.GotoAsync(Fixture.WebBaseUrl.ToString());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
    }

    /// <remarks>Copy of <c>EventManagementScenarios.CreateEventAsync</c> - see its remarks for the timeout reasoning.</remarks>
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
