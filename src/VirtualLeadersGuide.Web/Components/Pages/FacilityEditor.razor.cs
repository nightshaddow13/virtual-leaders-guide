using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Facilities;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class FacilityEditor
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiFacilityClient FacilityClient { get; set; } = default!;

    [Inject]
    private ApiFacilityTypeClient FacilityTypeClient { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    private PageState state = PageState.Loading;
    private FacilityFormModel? model;
    private IReadOnlyList<string> existingTypeNames = [];
    private Dictionary<string, Guid> existingTypeIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private string? saveErrorMessage;
    private bool isSaving;

    /// <remarks>
    /// Admin-only, the same flat <c>EventAccessView.IsAdmin</c> gate <c>Users.razor.cs</c> uses - this page
    /// has no route parameter to resolve first (unlike <c>ActivityEditor</c>'s <c>EventId</c>), so the gate
    /// runs in <see cref="OnInitializedAsync"/> rather than <c>OnParametersSetAsync</c>. Once past the gate,
    /// pre-loads every existing Facility Type via <see cref="ApiFacilityTypeClient.ListAsync"/> - the
    /// autofill's data source - rather than fetching it lazily as the Admin types, since the list is small
    /// and this avoids a round trip per keystroke.
    /// </remarks>
    protected override async Task OnInitializedAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        if (!new EventAccessView(authState.User).IsAdmin)
        {
            state = PageState.Denied;
            return;
        }

        try
        {
            (FacilityTypeReadOutcome outcome, IReadOnlyList<FacilityTypeDto> types) =
                await FacilityTypeClient.ListAsync(CancellationToken.None);

            if (outcome != FacilityTypeReadOutcome.Success)
            {
                state = PageState.Denied;
                return;
            }

            ApplyFacilityTypes(types);
        }
        catch (FacilityDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        model = new FacilityFormModel();
        state = PageState.Ready;
    }

    private void ApplyFacilityTypes(IReadOnlyList<FacilityTypeDto> types)
    {
        existingTypeNames = [.. types.Select(type => type.Name)];
        existingTypeIdsByName = types.ToDictionary(type => type.Name, type => type.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// Wired to <c>EditForm</c>'s <c>OnValidSubmit</c>, matching <c>ActivityEditor.razor.cs</c>'s pattern.
    /// Resolves <see cref="FacilityFormModel.TypeName"/> against the Facility Types already loaded into
    /// <see cref="existingTypeIdsByName"/> (case-insensitive exact match - the wireframe's "pick an existing
    /// type" path); on no match, creates a new one inline via <see cref="ApiFacilityTypeClient.CreateAsync"/>
    /// (the wireframe's "use as new" path) before creating the Facility itself. A
    /// <see cref="FacilityTypeWriteOutcome.Conflict"/> - another Admin created the identical name between
    /// this page's load and this Save - is treated as "use the existing one" by re-listing rather than a
    /// hard failure, since two Admins typing the same brand-new type at once is a benign race from the
    /// caller's point of view.
    /// </remarks>
    private async Task SaveAsync()
    {
        isSaving = true;
        saveErrorMessage = null;

        try
        {
            string typeName = (model!.TypeName ?? string.Empty).Trim();

            (bool resolved, Guid facilityTypeId) = await ResolveFacilityTypeIdAsync(typeName);
            if (!resolved)
            {
                return;
            }

            (FacilityWriteOutcome outcome, _, IReadOnlyList<string> pointers) = await FacilityClient.CreateAsync(
                model.Name ?? string.Empty, facilityTypeId, CancellationToken.None);

            if (outcome == FacilityWriteOutcome.Forbidden)
            {
                state = PageState.Denied;
                return;
            }

            if (outcome == FacilityWriteOutcome.Invalid)
            {
                saveErrorMessage = pointers.Count > 0
                    ? "That Facility Type no longer exists. Try again."
                    : "Something went wrong saving this Facility. Try again.";
                return;
            }

            NotificationService.Notify(NotificationSeverity.Success, "Facility created");
            NavigationManager.NavigateTo("dashboard/facilities", forceLoad: true);
        }
        catch (FacilityDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong saving this Facility. Try again.";
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <returns>
    /// <see langword="true"/> with the resolved id when an existing or newly-created Facility Type was
    /// found; <see langword="false"/> when the caller should stop (an error message or
    /// <see cref="PageState.Denied"/> has already been set).
    /// </returns>
    private async Task<(bool Resolved, Guid FacilityTypeId)> ResolveFacilityTypeIdAsync(string typeName)
    {
        if (existingTypeIdsByName.TryGetValue(typeName, out Guid existingId))
        {
            return (true, existingId);
        }

        (FacilityTypeWriteOutcome outcome, FacilityTypeDto? created) =
            await FacilityTypeClient.CreateAsync(typeName, CancellationToken.None);

        if (outcome == FacilityTypeWriteOutcome.Success)
        {
            return (true, created!.Id);
        }

        if (outcome == FacilityTypeWriteOutcome.Forbidden)
        {
            state = PageState.Denied;
            return (false, Guid.Empty);
        }

        return await ResolveAfterConflictAsync(typeName);
    }

    private async Task<(bool Resolved, Guid FacilityTypeId)> ResolveAfterConflictAsync(string typeName)
    {
        (FacilityTypeReadOutcome refreshOutcome, IReadOnlyList<FacilityTypeDto> refreshed) =
            await FacilityTypeClient.ListAsync(CancellationToken.None);

        if (refreshOutcome == FacilityTypeReadOutcome.Success)
        {
            ApplyFacilityTypes(refreshed);
        }

        if (existingTypeIdsByName.TryGetValue(typeName, out Guid resolvedId))
        {
            return (true, resolvedId);
        }

        saveErrorMessage = "Something went wrong saving this Facility. Try again.";
        return (false, Guid.Empty);
    }

    private sealed class FacilityFormModel
    {
        [Required(ErrorMessage = "Enter a name.")]
        [StringLength(200, ErrorMessage = "Name can't be longer than 200 characters.")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Enter or pick a type.")]
        [StringLength(200, ErrorMessage = "Type can't be longer than 200 characters.")]
        public string? TypeName { get; set; }
    }
}
