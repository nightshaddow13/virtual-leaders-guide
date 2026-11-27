using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Covers P8-2 (#166) dashboard creation of a Facility, including the Facility Type autofill's
/// resolve-or-create flow, and P8-3 (#167)'s list page. A successful create lands on
/// <c>/dashboard/facilities</c>, not the main dashboard (see <c>FacilityEditor.razor.cs</c>'s remarks) - P8-3
/// gave the Facility area somewhere of its own to land on. P8-4 (#168) adds editing a Facility's Name and Type
/// from that list. P8-5 (#169) adds deleting one, from the list row or the edit form, and a Director's refusal.
/// Unlike <see cref="ActivityManagementScenarios"/>,
/// a Facility has no owning Event to cascade-clean it (ADR-0066), so every Facility/Facility Type this class
/// creates is tracked explicitly via <see cref="E2ETestBase.TrackFacility"/>/<see cref="E2ETestBase.TrackFacilityType"/>
/// (ADR-0039's new P8-2 table rows) rather than relying on an Event's own teardown. Assertions go through
/// <see cref="AspireE2EFixture.Facilities"/> directly, not just the UI's own success notification - the AC's
/// "clear success confirmation" criterion extends to the row actually existing, not just the page saying so.
/// The delete scenarios read the store too, since a vanished row alone would also pass if the grid merely failed
/// to load. The Director scenario sends a real Director token to the deployed Api, which
/// <c>FacilitiesResourceShould</c>'s test authentication never exercises, and asserts exactly 403 so a token the
/// Api rejects outright (401) can't pass as a refusal.
/// </remarks>
public class FacilityManagementScenarios : E2ETestBase
{
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
                FacilityListUrl(), InteractiveUrl());

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
                FacilityListUrl(), InteractiveUrl());

            (Guid firstFacilityId, Guid facilityTypeId) = await AssertFacilityAndTypeExistAsync(firstName, typeName);
            TrackFacility(firstFacilityId);
            TrackFacilityType(facilityTypeId);

            await Page.GotoAsync(NewFacilityUrl());
            await Page.Locator("#Name").FillAsync(secondName);
            await Page.Locator("#TypeName").FillAsync(typeName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                FacilityListUrl(), InteractiveUrl());

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
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given a Director, when navigating directly to the Facility list, then they are denied")]
    public async Task GivenADirector_WhenNavigatingDirectlyToTheFacilityList_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Facility List Denial Host");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(eventId);

            await Page.GotoAsync(FacilityListUrl());

            await Expect(Page.GetByText("Only Admins can manage Facilities.")).ToBeVisibleAsync(
                InteractiveVisible());
        });

    /// <remarks>
    /// <c>SiteHeaderShould</c> only covers which links the header renders. Reaches the list by clicking that link rather than
    /// navigating directly, so a regression removing or mis-routing the link fails this test, not just a
    /// direct-navigation one.
    /// </remarks>
    [Fact(DisplayName = "Given an Admin who created a Facility, when they open the Facility list from the header link, then they see its Name and Type")]
    public async Task GivenAnAdminWhoCreatedAFacility_WhenTheyOpenTheFacilityListFromTheHeaderLink_ThenTheySeeItsNameAndType() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string name = $"e2e-Camp Blackhawk {Guid.NewGuid():n}";
            string typeName = $"e2e-Camp {Guid.NewGuid():n}";

            await Page.GotoAsync(NewFacilityUrl());
            await Page.Locator("#Name").FillAsync(name);
            await Page.Locator("#TypeName").FillAsync(typeName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                FacilityListUrl(), InteractiveUrl());

            (Guid facilityId, Guid facilityTypeId) = await AssertFacilityAndTypeExistAsync(name, typeName);
            TrackFacility(facilityId);
            TrackFacilityType(facilityTypeId);

            await Page.GotoAsync(DashboardUrl());
            await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Facilities" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                FacilityListUrl(), InteractiveUrl());

            await Expect(Page.GetByText(name)).ToBeVisibleAsync(
                InteractiveVisible());
            await Expect(Page.GetByText(typeName)).ToBeVisibleAsync();
        });

    /// <remarks>
    /// Creates the Facility through the real form (this suite's invariant - <see cref="FacilitiesApiClient"/>
    /// has no <c>Create*Async</c>), then reaches the edit form by clicking the list row's icon-only Edit button
    /// rather than navigating directly, so a regression mis-routing that button fails here. Types a
    /// brand-new Type on edit so both the PATCH and the inline Type creation are exercised. The "Changes
    /// saved" toast isn't asserted: <c>FacilityEditor</c> navigates with <c>forceLoad: true</c>, which tears
    /// down the circuit that owns the toast - <c>FacilityEditorShould</c> covers it instead. The editor creates
    /// the new Type before it PATCHes the Facility, so that Type can exist even when a later step fails; the
    /// <c>finally</c> tracks it however the body exits (ADR-0039).
    /// </remarks>
    [Fact(DisplayName = "Given an Admin with a Facility, when they edit its Name and Type from the list, then the list shows the new values")]
    public async Task GivenAnAdminWithAFacility_WhenTheyEditItsNameAndTypeFromTheList_ThenTheListShowsTheNewValues() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            string originalName = $"e2e-Camp Blackhawk {Guid.NewGuid():n}";
            string originalTypeName = $"e2e-Camp {Guid.NewGuid():n}";
            string renamedTo = $"e2e-Camp Pinewood {Guid.NewGuid():n}";
            string newTypeName = $"e2e-Retreat Center {Guid.NewGuid():n}";

            await Page.GotoAsync(NewFacilityUrl());
            await Page.Locator("#Name").FillAsync(originalName);
            await Page.Locator("#TypeName").FillAsync(originalTypeName);
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                FacilityListUrl(), InteractiveUrl());

            (Guid facilityId, Guid originalTypeId) = await AssertFacilityAndTypeExistAsync(originalName, originalTypeName);
            TrackFacility(facilityId);
            TrackFacilityType(originalTypeId);

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = originalName });
            await Expect(row).ToBeVisibleAsync(InteractiveVisible());
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                EditFacilityUrl(facilityId), InteractiveUrl());

            await Expect(Page.Locator("#Name")).ToHaveValueAsync(
                originalName, InteractiveValue());
            await Expect(Page.Locator("#TypeName")).ToHaveValueAsync(originalTypeName);

            try
            {
                await Page.Locator("#Name").FillAsync(renamedTo);
                await Page.Locator("#TypeName").FillAsync(newTypeName);
                await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
                await Expect(Page).ToHaveURLAsync(
                    FacilityListUrl(), InteractiveUrl());

                await Expect(Page.GetByText(renamedTo)).ToBeVisibleAsync(
                    InteractiveVisible());
                await Expect(Page.GetByText(newTypeName)).ToBeVisibleAsync();
                await Expect(Page.GetByText(originalName)).Not.ToBeVisibleAsync();

                (Guid editedFacilityId, Guid newTypeId) = await AssertFacilityAndTypeExistAsync(renamedTo, newTypeName);
                Assert.Equal(facilityId, editedFacilityId);
                Assert.NotEqual(originalTypeId, newTypeId);
            }
            finally
            {
                IReadOnlyList<(Guid Id, string Name)> types = await Fixture.Facilities.ListE2EFacilityTypesAsync(CancellationToken.None);
                foreach ((Guid typeId, _) in types.Where(type => type.Name == newTypeName))
                {
                    TrackFacilityType(typeId);
                }
            }
        });

    [Fact(DisplayName = "Given a Director, when navigating directly to the edit-Facility form, then they are denied")]
    public async Task GivenADirector_WhenNavigatingDirectlyToTheEditFacilityForm_ThenTheyAreDenied() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid eventId, _) = await CreateEventAsync("Facility Edit Denial Host");
            await SignOutAsync();

            await CreateAndSignInDirectorAsync(eventId);

            await Page.GotoAsync(EditFacilityUrl(Guid.NewGuid()));

            await Expect(Page.GetByText("Only Admins can manage Facilities.")).ToBeVisibleAsync(
                InteractiveVisible());
        });

    [Fact(DisplayName = "Given an Admin with a Facility, when they delete it from the list and confirm, then it is gone from the list and the store")]
    public async Task GivenAnAdminWithAFacility_WhenTheyDeleteItFromTheListAndConfirm_ThenItIsGoneFromTheListAndTheStore() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid facilityId, string name) = await CreateFacilityThroughTheFormAsync();

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await Expect(row).ToBeVisibleAsync(InteractiveVisible());
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            await ConfirmDeleteAsync();

            await Expect(row).Not.ToBeVisibleAsync(InteractiveVisible());
            await AssertFacilityIsGoneAsync(facilityId);
        });

    [Fact(DisplayName = "Given an Admin with a Facility, when they delete it from its edit form and confirm, then they land on the list and it is gone from the store")]
    public async Task GivenAnAdminWithAFacility_WhenTheyDeleteItFromItsEditFormAndConfirm_ThenTheyLandOnTheListAndItIsGoneFromTheStore() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid facilityId, string name) = await CreateFacilityThroughTheFormAsync();

            ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
            await Expect(row).ToBeVisibleAsync(InteractiveVisible());
            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(
                EditFacilityUrl(facilityId), InteractiveUrl());
            await Expect(Page.Locator("#Name")).ToHaveValueAsync(
                name, InteractiveValue());

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete facility" }).ClickAsync();
            await ConfirmDeleteAsync();

            await Expect(Page).ToHaveURLAsync(
                FacilityListUrl(), InteractiveUrl());
            await AssertFacilityIsGoneAsync(facilityId);
        });

    [Fact(DisplayName = "Given a Director, when they try to delete a Facility through the Api, then they are refused and it still exists")]
    public async Task GivenADirector_WhenTheyTryToDeleteAFacilityThroughTheApi_ThenTheyAreRefusedAndItStillExists() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();
            (Guid facilityId, _) = await CreateFacilityThroughTheFormAsync();

            System.Net.HttpStatusCode status =
                await Fixture.Facilities.DeleteFacilityAsDirectorAsync(facilityId, CancellationToken.None);

            Assert.Equal(System.Net.HttpStatusCode.Forbidden, status);
            IReadOnlyList<(Guid Id, string Name)> facilities = await Fixture.Facilities.ListE2EFacilitiesAsync(CancellationToken.None);
            Assert.Contains(facilities, facility => facility.Id == facilityId);
        });

    /// <summary>Creates a tracked Facility through the real form, leaving the page on the Facility list.</summary>
    private async Task<(Guid FacilityId, string Name)> CreateFacilityThroughTheFormAsync()
    {
        string name = $"e2e-Camp Blackhawk {Guid.NewGuid():n}";
        string typeName = $"e2e-Camp {Guid.NewGuid():n}";

        await Page.GotoAsync(NewFacilityUrl());
        await Page.Locator("#Name").FillAsync(name);
        await Page.Locator("#TypeName").FillAsync(typeName);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create facility" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            FacilityListUrl(), InteractiveUrl());

        (Guid facilityId, Guid facilityTypeId) = await AssertFacilityAndTypeExistAsync(name, typeName);
        TrackFacility(facilityId);
        TrackFacilityType(facilityTypeId);
        return (facilityId, name);
    }

    private async Task ConfirmDeleteAsync()
    {
        ILocator dialog = Page.Locator(".rz-dialog-content");
        await Expect(dialog).ToBeVisibleAsync(InteractiveVisible());
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
    }

    private async Task AssertFacilityIsGoneAsync(Guid facilityId)
    {
        IReadOnlyList<(Guid Id, string Name)> facilities = await Fixture.Facilities.ListE2EFacilitiesAsync(CancellationToken.None);
        Assert.DoesNotContain(facilities, facility => facility.Id == facilityId);
    }

    private string DashboardUrl() => new Uri(Fixture.WebBaseUrl, "dashboard").ToString();

    private string EditFacilityUrl(Guid facilityId) =>
        new Uri(Fixture.WebBaseUrl, $"dashboard/facilities/{facilityId}/edit").ToString();

    private string FacilityListUrl() => new Uri(Fixture.WebBaseUrl, "dashboard/facilities").ToString();

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
