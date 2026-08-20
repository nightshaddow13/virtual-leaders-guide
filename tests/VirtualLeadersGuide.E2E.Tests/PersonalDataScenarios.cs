using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Scoped to <c>PersonalData.razor</c> and the <c>DeletePersonalData.razor</c> page it links to (P2.1-5,
/// #63) - both halves of the same "personal data" screen, the same concern-scoped naming
/// <see cref="LoginPageScenarios"/>'s remarks explain for this project (ADR-0029). Password changes live in
/// <see cref="ChangePasswordScenarios"/> instead.
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class PersonalDataScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given a signed-in user, when they download their personal data, then a PersonalData.json file for that user is downloaded")]
    public async Task GivenASignedInUser_WhenTheyDownloadTheirPersonalData_ThenAPersonalDataJsonFileForThatUserIsDownloaded() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-download-personal-data-{Guid.NewGuid():n}@example.test";
            IdentityUserDto user = await Fixture.IdentityApi.CreateUserAsync(
                email, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "Account/Manage/PersonalData").ToString());

            IDownload download = await Page.RunAndWaitForDownloadAsync(async () =>
                await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Download" }).ClickAsync());

            Assert.Equal("PersonalData.json", download.SuggestedFilename);

            await using Stream stream = await download.CreateReadStreamAsync();
            Dictionary<string, string>? personalData =
                await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream);

            Assert.Equal(email, personalData!["Email"]);
            Assert.Equal(user.Id, personalData["Id"]);
        });

    /// <remarks>
    /// Seeds a plain no-role user - a Director's or Admin's Role grant cascade-deleting along with them is
    /// DB-enforced FK behavior (<c>UserRole.UserId</c>'s <c>OnDelete(DeleteBehavior.Cascade)</c>), not
    /// application logic, so it isn't this suite's job to prove (P2.1-5 planning notes, #63).
    /// </remarks>
    [Fact(DisplayName = "Given a signed-in user, when they delete their personal data with the correct password, then their account no longer exists and they are signed out")]
    public async Task GivenASignedInUser_WhenTheyDeleteTheirPersonalDataWithTheCorrectPassword_ThenTheirAccountNoLongerExistsAndTheyAreSignedOut() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-delete-personal-data-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "Account/Manage/DeletePersonalData").ToString());
            await Page.Locator("#Input\\.Password").FillAsync(TestCredentials.KnownPassword);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete data and close my account" })
                .ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Regex("Account/Login"));

            Assert.False(await Fixture.IdentityApi.ExistsAsync(email, CancellationToken.None));

            await Page.GotoAsync(Fixture.WebBaseUrl.ToString());
            await Expect(Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Sign in" })).ToBeVisibleAsync();
        });
}
