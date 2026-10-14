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

    private const string FacilityTypeIdPointer = "/data/attributes/facilityTypeId";

    /// <summary>The Facility being edited, or <see langword="null"/> when creating a new one.</summary>
    /// <remarks>
    /// <see langword="null"/> on <c>.../facilities/new</c>; set on <c>.../facilities/{Id}/edit</c> - the same
    /// create-or-edit shape as <c>EventEditor.Id</c>/<c>InfoPageEditor.InfoPageId</c>, except the edit route
    /// carries an <c>/edit</c> suffix. <c>/dashboard/facilities/{Id}</c> is reserved for the P9 detail page
    /// (ADR-0073).
    /// </remarks>
    [Parameter]
    public Guid? Id { get; set; }

    private bool IsEdit => Id is not null;

    private string Heading => IsEdit ? "Edit facility" : "New facility";

    private string SubmitText => IsEdit ? "Save changes" : "Create facility";

    private string BreadcrumbTail => IsEdit ? facilityName ?? string.Empty : "NEW";

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Missing, Ready }

    private PageState state = PageState.Loading;
    private FacilityFormModel? model;
    private string? facilityName;
    private IReadOnlyList<string> existingTypeNames = [];
    private Dictionary<string, Guid> existingTypeIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Guid, string> typeNamesById = new();
    private string? saveErrorMessage;
    private bool isSaving;
    private bool hasLoaded;
    private Guid? loadedForId;

    /// <remarks>
    /// Admin-only, the same flat <c>EventAccessView.IsAdmin</c> gate <c>Users.razor.cs</c> uses. Runs in
    /// <see cref="OnParametersSetAsync"/> rather than <c>OnInitializedAsync</c> because the edit route carries
    /// an <see cref="Id"/> to resolve, like <c>InfoPageEditor</c>. Once past the gate, pre-loads every
    /// existing Facility Type via <see cref="ApiFacilityTypeClient.ListAsync"/> - the autofill's data source,
    /// and the lookup that turns an edited Facility's <see cref="FacilityDto.FacilityTypeId"/> back into the
    /// name shown in the field - rather than fetching it lazily as the Admin types, since the list is small
    /// and this avoids a round trip per keystroke. When editing, a Facility that doesn't resolve falls into
    /// <see cref="PageState.Missing"/>, not <see cref="PageState.Denied"/>: a global resource has no
    /// Event-scoping that could make "missing" and "not yours" look the same. Blazor re-runs this on every
    /// parameter or cascading-parameter update, so it loads once per <see cref="Id"/> (<c>hasLoaded</c>/
    /// <c>loadedForId</c>): a re-render with the same <see cref="Id"/> must not rebuild <c>model</c> and discard
    /// the Admin's unsaved edits. A <em>different</em> <see cref="Id"/> on the same instance resets the page
    /// to loading first, so the previous Facility's form, error alert and saving flag can't linger - or take a
    /// Save - while the next one loads. The Type list and the Facility are two separate reads, so a Facility
    /// whose Type id the list doesn't know is possible; the field is then left empty with an alert saying why,
    /// rather than leaving the Admin to wonder where the Type went.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationStateTask is null || (hasLoaded && loadedForId == Id))
        {
            return;
        }

        hasLoaded = true;
        loadedForId = Id;
        state = PageState.Loading;
        model = null;
        facilityName = null;
        saveErrorMessage = null;
        isSaving = false;

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

            if (!IsEdit)
            {
                model = new FacilityFormModel();
                state = PageState.Ready;
                return;
            }

            (FacilityReadOutcome facilityOutcome, FacilityDto? facility) =
                await FacilityClient.GetAsync(Id!.Value, CancellationToken.None);

            if (facilityOutcome == FacilityReadOutcome.NotFound)
            {
                state = PageState.Missing;
                return;
            }

            if (facilityOutcome != FacilityReadOutcome.Success || facility is null)
            {
                state = PageState.Denied;
                return;
            }

            string? currentTypeName = typeNamesById.GetValueOrDefault(facility.FacilityTypeId);
            facilityName = facility.Name;
            model = new FacilityFormModel { Name = facility.Name, TypeName = currentTypeName };

            if (currentTypeName is null)
            {
                saveErrorMessage = "This Facility's current Type couldn't be found. Pick a Type and save.";
            }

            state = PageState.Ready;
        }
        catch (FacilityDataUnavailableException)
        {
            state = PageState.Unavailable;
        }
    }

    private void ApplyFacilityTypes(IReadOnlyList<FacilityTypeDto> types)
    {
        existingTypeNames = [.. types.Select(type => type.Name)];
        existingTypeIdsByName = types.ToDictionary(type => type.Name, type => type.Id, StringComparer.OrdinalIgnoreCase);
        typeNamesById = types.ToDictionary(type => type.Id, type => type.Name);
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
    /// caller's point of view. The same resolve-or-create step serves both modes - an Admin editing a
    /// Facility can equally type a brand-new Type - and only the final write differs: a create when
    /// <see cref="Id"/> is <see langword="null"/>, an update otherwise. A <see cref="FacilityWriteOutcome.Forbidden"/>
    /// on either swaps the page to <see cref="PageState.Denied"/> and a
    /// <see cref="FacilityWriteOutcome.NotFound"/> to <see cref="PageState.Missing"/>; a Type left unreferenced
    /// by an edit is accepted, not reaped (ADR-0071).
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

            string name = model.Name ?? string.Empty;
            FacilityWriteOutcome outcome;
            IReadOnlyList<string> pointers;
            if (IsEdit)
            {
                (outcome, pointers) = await FacilityClient.UpdateAsync(Id!.Value, name, facilityTypeId, CancellationToken.None);
            }
            else
            {
                (outcome, _, pointers) = await FacilityClient.CreateAsync(name, facilityTypeId, CancellationToken.None);
            }

            if (outcome == FacilityWriteOutcome.Forbidden)
            {
                state = PageState.Denied;
                return;
            }

            if (outcome == FacilityWriteOutcome.NotFound)
            {
                state = PageState.Missing;
                return;
            }

            if (outcome == FacilityWriteOutcome.Invalid)
            {
                if (pointers.Contains(FacilityTypeIdPointer))
                {
                    await RefreshFacilityTypesAsync();
                    saveErrorMessage = "That Facility Type no longer exists. Try again.";
                }
                else
                {
                    saveErrorMessage = "Something went wrong saving this Facility. Try again.";
                }

                return;
            }

            NotificationService.Notify(NotificationSeverity.Success, IsEdit ? "Changes saved" : "Facility created");
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

    /// <remarks>
    /// Re-reads the Facility Types into the autofill's lookups. Also called after Api rejects a Save over an
    /// unknown Type id, so a retry resolves against the current list instead of re-sending the same dead id.
    /// </remarks>
    private async Task RefreshFacilityTypesAsync()
    {
        (FacilityTypeReadOutcome outcome, IReadOnlyList<FacilityTypeDto> types) =
            await FacilityTypeClient.ListAsync(CancellationToken.None);

        if (outcome == FacilityTypeReadOutcome.Success)
        {
            ApplyFacilityTypes(types);
        }
    }

    private async Task<(bool Resolved, Guid FacilityTypeId)> ResolveAfterConflictAsync(string typeName)
    {
        await RefreshFacilityTypesAsync();

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
