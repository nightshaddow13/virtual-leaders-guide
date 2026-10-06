using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P8-2 (#166): dashboard creation of a Facility, including the Facility Type autofill's
/// resolve-or-create flow. Create-only - there is no Facility list or edit page yet (P8-3/#167, P8-4/#168),
/// so a successful create lands on the main dashboard rather than a per-Facility URL (see
/// <c>FacilityEditor.razor.cs</c>'s remarks). Unlike <see cref="ActivityManagementScenarios"/>, a Facility
/// has no owning Event to cascade-clean it (ADR-0066), so every Facility/Facility Type this class creates is
/// tracked explicitly via <see cref="E2ETestBase.TrackFacility"/>/<see cref="E2ETestBase.TrackFacilityType"/>
/// (ADR-0039's new P8-2 table rows) rather than relying on an Event's own teardown. Assertions go through
/// <see cref="AspireE2EFixture.Facilities"/> directly, not just the UI's own success notification - the AC's
/// "clear success confirmation" criterion extends to the row actually existing, not just the dashboard
/// saying so.
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class FacilityManagementScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>See <see cref="EventManagementScenarios.InteractiveTimeoutMs"/>'s identical remarks - every page here is <c>InteractiveServer</c> too.</remarks>
    private const int InteractiveTimeoutMs = 15_000;

    [Fact(DisplayName = "Given an Admin, when they create a Facility by typing a brand-new Type name, then both the Facility and the new Type exist")]
    public async Task GivenAnAdmin_WhenTheyCreateAFacilityByTypingABrandNewTypeName_ThenBothTheFacilityAndTheNewTypeExist() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string name = $"e2e-Camp Blackhawk {Guid.NewGuid():n}";
            string typeName = $"e2e-Retreat Center {Guid.NewGuid():n}";

            await Page.GotoAsync(NewFacilityUrl());
            await Page.Locator("#Name").FillAsync(name);
            await Page.Locator("#TypeName").FillAsync(typeName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                DashboardUrl(), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            (Guid facilityId, Guid facilityTypeId) = await AssertFacilityAndTypeExistAsync(name, typeName);
            TrackFacility(facilityId);
            TrackFacilityType(facilityTypeId);
        });

    [Fact(DisplayName = "Given an existing Facility Type, when an Admin creates another Facility picking it via the autofill, then no new Type is created")]
    public async Task GivenAnExistingFacilityType_WhenAnAdminCreatesAnotherFacilityPickingItViaTheAutofill_ThenNoNewTypeIsCreated() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string typeName = $"e2e-Camp {Guid.NewGuid():n}";
            string firstName = $"e2e-Camp Blackhawk {Guid.NewGuid():n}";
            string secondName = $"e2e-Camp Winnebago {Guid.NewGuid():n}";

            await Page.GotoAsync(NewFacilityUrl());
            await Page.Locator("#Name").FillAsync(firstName);
            await Page.Locator("#TypeName").FillAsync(typeName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                DashboardUrl(), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            (Guid firstFacilityId, Guid facilityTypeId) = await AssertFacilityAndTypeExistAsync(firstName, typeName);
            TrackFacility(firstFacilityId);
            TrackFacilityType(facilityTypeId);

            await Page.GotoAsync(NewFacilityUrl());
            await Page.Locator("#Name").FillAsync(secondName);
            await Page.Locator("#TypeName").FillAsync(typeName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                DashboardUrl(), new PageAssertionsToHaveURLOptions { Timeout = InteractiveTimeoutMs });

            (Guid secondFacilityId, Guid reusedFacilityTypeId) = await AssertFacilityAndTypeExistAsync(secondName, typeName);
            TrackFacility(secondFacilityId);
            Assert.Equal(facilityTypeId, reusedFacilityTypeId);

            IReadOnlyList<(Guid Id, string Name)> types = await Fixture.Facilities.ListE2EFacilityTypesAsync(CancellationToken.None);
            Assert.Single(types, type => type.Name == typeName);
        });

    [Fact(DisplayName = "Given a Director, when navigating directly to the new-Facility form, then they are denied")]
    public async Task GivenADirector_WhenNavigatingDirectlyToTheNewFacilityForm_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Facility Denial Host");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(eventId);

            await Page.GotoAsync(NewFacilityUrl());

            await Expect(Page.GetByText("Only Admins can manage Facilities.")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = InteractiveTimeoutMs });
        });

    private string DashboardUrl() => new Uri(Fixture.WebBaseUrl, "dashboard").ToString();

    private string NewFacilityUrl() => new Uri(Fixture.WebBaseUrl, "dashboard/facilities/new").ToString();

    /// <summary>
    /// Confirms both rows exist via <see cref="AspireE2EFixture.Facilities"/> directly, not just the UI's
    /// own success path - a Facility carries no <c>facilityTypeId</c> in its list attributes this client
    /// reads, so the Type's existence is confirmed by name instead, same discriminator (<c>e2e-</c>-prefixed,
    /// guid-suffixed) every other call site in this class relies on for uniqueness.
    /// </summary>
    private async Task<(Guid FacilityId, Guid FacilityTypeId)> AssertFacilityAndTypeExistAsync(string facilityName, string typeName)
    {
        IReadOnlyList<(Guid Id, string Name)> facilities = await Fixture.Facilities.ListE2EFacilitiesAsync(CancellationToken.None);
        (Guid Id, string Name) facility = Assert.Single(facilities, f => f.Name == facilityName);

        IReadOnlyList<(Guid Id, string Name)> types = await Fixture.Facilities.ListE2EFacilityTypesAsync(CancellationToken.None);
        (Guid Id, string Name) facilityType = Assert.Single(types, t => t.Name == typeName);

        return (facility.Id, facilityType.Id);
    }

    /// <remarks>Kept local, not shared - see <see cref="ActivityManagementScenarios"/>'s identical copy and its own header remarks.</remarks>
    private async Task<IdentityUserDto> CreateAndSignInDirectorAsync(Guid eventId)
    {
        IdentityUserDto director = await CreateTrackedUserAsync("e2e-director", CancellationToken.None);
        await Fixture.IdentityApi.GrantDirectorAsync(director.Id, eventId, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, director.Email!, TestCredentials.KnownPassword);
        return director;
    }
}
