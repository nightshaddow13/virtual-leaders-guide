using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P2-12 (#43): inviting a Director by email from <c>/dashboard/users</c>, completing account setup
/// at <c>/setup</c>, assigning the invited Director to an Event from the Event's own Directors section
/// (ADR-0035 - never the reverse), and the resend/revoke actions on a pending invite. Also covers P2-18
/// (#113): removing a Director's Event-scoped access from that same section - the two removal scenarios
/// seed their throwaway Director's Grant directly via <c>Fixture.IdentityApi.GrantDirectorAsync</c> rather
/// than through <see cref="AddDirectorToEventAsync"/>'s dropdown UI, since the subject under test is the
/// Remove action, not how the Grant got there in the first place.
/// <see cref="AspireE2EFixture.EmailSink"/> intercepts the invite email the same way
/// <see cref="PasswordResetScenarios"/> intercepts a reset link. <c>IdentityApiClient.GrantDirectorAsync</c>
/// (added for P2-9, #18) is deliberately unused here - the point of these scenarios is that the invite UI
/// itself creates the Role/Grant rows, not a seeding shortcut. Every invited User is tracked for cleanup via
/// <see cref="E2ETestBase.TrackUserByEmailAsync"/> right after the invite email lands, since the UI (not
/// <see cref="IdentityApiClient.CreateUserAsync"/>) is what creates the row - a revoked invite's own delete
/// makes a second, tracked delete a tolerated no-op, not a double-delete failure (ADR-0039).
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
            await TrackUserByEmailAsync(email);

            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);
            Assert.Equal(SentEmailKinds.DirectorInvite, inviteEmail.Kind);
            Assert.Contains("/setup?", inviteEmail.Payload, StringComparison.Ordinal);

            await SearchUsersAsync(email);
            await AssertUserRowStateAsync(email, "Invited");
        });

    [Fact(DisplayName = "Given an invited Director, when they complete account setup, then they can sign in and see an empty Events list")]
    public async Task GivenAnInvitedDirector_WhenTheyCompleteAccountSetup_ThenTheyCanSignInAndSeeAnEmptyEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-setup-{Guid.NewGuid():n}@example.test";
            await OpenInviteDialogAndSendAsync(email, displayName: null);
            await TrackUserByEmailAsync(email);

            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);
            await SignOutAsync();

            await Page.GotoAsync(inviteEmail.Payload);
            await SubmitSetupAsync(TestCredentials.KnownPassword);
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/Login").ToString());

            await SignInAndGoToDashboardAsync(email, TestCredentials.KnownPassword);

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

            (Guid eventId, string eventName) = await CreateEventAsync("Spring Kickoff");
            await OpenInviteDialogAndSendAsync(email, displayName: null);
            await TrackUserByEmailAsync(email);
            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);

            await SignOutAsync();
            await Page.GotoAsync(inviteEmail.Payload);
            await SubmitSetupAsync(TestCredentials.KnownPassword);

            await SignInAsAdminAsync();
            await Page.GotoAsync(EventEditorUrl(eventId));
            await AddDirectorToEventAsync(email);
            await Expect(Page.GetByText(email).First).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

            await SignOutAsync();
            await SignInAndGoToDashboardAsync(email, TestCredentials.KnownPassword);

            await Expect(Page.GetByText(eventName)).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an email that already belongs to a User, when an Admin tries to invite it again, then the existing account is shown and no email is sent")]
    public async Task GivenAnEmailThatAlreadyBelongsToAUser_WhenAnAdminTriesToInviteItAgain_ThenTheExistingAccountIsShownAndNoEmailIsSent() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            IdentityUserDto existing = await CreateTrackedUserAsync("e2e-invite-duplicate", CancellationToken.None);

            await OpenInviteDialogAsync();
            await Page.Locator("#Email").FillAsync(existing.Email!);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();

            await Expect(Page.GetByText("already exists")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open this user" }))
                .ToBeVisibleAsync();

            await EstablishNoEmailWasWrittenHappensBeforeAsync();
            Assert.False(Fixture.EmailSink.HasEmailFor(existing.Email!));
        });

    [Fact(DisplayName = "Given a pending invite, when an Admin resends it, then a second email arrives and the first link no longer works")]
    public async Task GivenAPendingInvite_WhenAnAdminResendsIt_ThenASecondEmailArrivesAndTheFirstLinkNoLongerWorks() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string email = $"e2e-invite-resend-{Guid.NewGuid():n}@example.test";
            await OpenInviteDialogAndSendAsync(email, displayName: null);
            await TrackUserByEmailAsync(email);
            SentEmailDto firstInvite = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);

            await SearchUsersAsync(email);
            await OpenUserRowAsync(email);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Resend email" }).ClickAsync();

            SentEmailDto secondInvite = await Fixture.EmailSink.WaitForEmailAsync(email, firstInvite, CancellationToken.None);

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
            await TrackUserByEmailAsync(email);
            SentEmailDto inviteEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);

            await SearchUsersAsync(email);
            await OpenUserRowAsync(email);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Revoke invite" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, "dashboard/users").ToString(),
                new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });
            await FilterUsersAsync(email);
            await Expect(Page.GetByText(email)).Not.ToBeVisibleAsync();

            await SignOutAsync();
            await Page.GotoAsync(inviteEmail.Payload);
            await Expect(Page.GetByText("Invalid invite")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given an Admin viewing an Event's Directors list, when they remove a Director and confirm, then the Director disappears from that Event's list")]
    public async Task GivenAnAdminViewingAnEventsDirectorsList_WhenTheyRemoveADirectorAndConfirm_ThenTheDirectorDisappearsFromThatEventsList() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string _) = await CreateEventAsync("Remove Director");
            IdentityUserDto director = await CreateTrackedUserAsync("e2e-remove-director", CancellationToken.None);
            await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Remove {director.Email}", Exact = true })
                .ClickAsync();

            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await Expect(dialog).ToContainTextAsync(director.Email!);
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove", Exact = true }).ClickAsync();

            await Expect(Page.Locator(".vlg-directors-list").GetByText(director.Email!)).Not.ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    [Fact(DisplayName = "Given the remove-Director dialog is open, when an Admin cancels instead, then the Director stays assigned")]
    public async Task GivenTheRemoveDirectorDialogIsOpen_WhenAnAdminCancelsInstead_ThenTheDirectorStaysAssigned() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, string _) = await CreateEventAsync("Cancel Remove Director");
            IdentityUserDto director = await CreateTrackedUserAsync("e2e-cancel-remove-director", CancellationToken.None);
            await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

            await Page.GotoAsync(EventEditorUrl(eventId));
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Remove {director.Email}", Exact = true })
                .ClickAsync();

            ILocator dialog = Page.Locator(".rz-dialog-content");
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel", Exact = true }).ClickAsync();

            await Expect(dialog).Not.ToBeVisibleAsync();
            await Expect(Page.Locator(".vlg-directors-list").GetByText(director.Email!)).ToBeVisibleAsync(
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
        await TrackUserByEmailAsync(barrierEmail);
        await Fixture.EmailSink.WaitForEmailAsync(barrierEmail, CancellationToken.None);
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

    /// <summary>Navigates to the Users screen and filters the grid, without asserting on the result - see <see cref="SearchUsersAsync"/> for the common "and it's there" case.</summary>
    /// <remarks>
    /// <c>RadzenTextBox</c>'s <c>Change</c> event (which reloads the grid) fires on blur, not on every
    /// keystroke - <c>FillAsync</c> alone dispatches an <c>input</c> event but never blurs the field, so the
    /// grid never re-queries. Pressing Tab afterward blurs it and triggers the reload.
    /// </remarks>
    private async Task FilterUsersAsync(string search)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/users").ToString());
        ILocator searchBox = Page.GetByPlaceholder("Search email or name");
        await searchBox.FillAsync(search);
        await searchBox.PressAsync("Tab");
    }

    /// <summary>Filters the Users grid to <paramref name="search"/> and asserts a matching row appears.</summary>
    private async Task SearchUsersAsync(string search)
    {
        await FilterUsersAsync(search);
        await Expect(Page.GetByText(search)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
    }

    /// <remarks>
    /// Scoped to the one row containing <paramref name="email"/>, not the grid as a whole - since ADR-0039,
    /// <see cref="AspireE2EFixture.InvitedEmail"/> sits permanently in the grid with the same "Invited" state
    /// text this method checks for, and <see cref="SearchUsersAsync"/>'s own "wait for the target text to
    /// appear" only proves that row is rendered, not that Radzen's own async filter reload has finished
    /// removing every other row yet - a bare grid-wide <c>GetByText(state)</c> can catch that still-settling
    /// window and see two matches. Row-scoping sidesteps the race entirely rather than trying to out-wait it.
    /// </remarks>
    private async Task AssertUserRowStateAsync(string email, string state) =>
        await Expect(UserRowLocator(email).GetByText(state)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });

    /// <summary>Clicks the "Open" button in the one row containing <paramref name="email"/>.</summary>
    /// <remarks>
    /// Same race as <see cref="AssertUserRowStateAsync"/>'s own remarks explain, but worse here: <c>ClickAsync</c>
    /// evaluates its locator once and throws immediately on ambiguity, unlike an <c>Expect(...).ToBeVisibleAsync()</c>
    /// assertion, which retries until the page settles or its timeout expires. A bare page-wide "Open" button
    /// locator is now reliably ambiguous - every fixture account's own row renders one too - not just
    /// occasionally, since ADR-0039 made those rows permanent.
    /// </remarks>
    private async Task OpenUserRowAsync(string email) =>
        await UserRowLocator(email).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open" }).ClickAsync();

    private ILocator UserRowLocator(string email) =>
        Page.GetByRole(AriaRole.Row).Filter(new LocatorFilterOptions { HasText = email });

    private async Task SubmitSetupAsync(string password)
    {
        await Page.Locator("#Input\\.Password").FillAsync(password);
        await Page.Locator("#Input\\.ConfirmPassword").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Activate account" }).ClickAsync();
    }

    /// <remarks>
    /// Radzen's dropdown renders its option list in a popup keyed by visible text - <c>EventEditor.razor</c>'s
    /// picker uses each candidate's email as <c>TextProperty</c> (ADR-0035), so the option's accessible name
    /// is the email itself. <c>Escape</c> closes the popup, but its matching option stays in the DOM (just
    /// hidden) even after that - a caller asserting the email now appears among this Event's Directors still
    /// gets a strict-mode violation (two matches) unless it targets <c>.First</c> rather than a single match.
    /// </remarks>
    private async Task AddDirectorToEventAsync(string directorEmail)
    {
        await Page.Locator(".rz-dropdown").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = directorEmail }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add" }).ClickAsync();
    }

    /// <remarks>
    /// <see cref="LoginPage.SignInAsync"/> doesn't request a return URL, so sign-in lands on Home - the
    /// explicit navigation afterward matches <c>EventManagementScenarios</c>' own
    /// <c>CreateAndSignInDirectorAsync</c> usage.
    /// </remarks>
    private async Task SignInAndGoToDashboardAsync(string email, string password)
    {
        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, password);
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
    }
}
