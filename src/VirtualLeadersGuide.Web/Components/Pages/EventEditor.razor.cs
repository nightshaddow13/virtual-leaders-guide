using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Radzen;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Directors;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Time;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class EventEditor
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiDirectorClient DirectorClient { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private TooltipService TooltipService { get; set; } = default!;

    [Inject]
    private BrowserTimeZoneAccessor TimeZoneAccessor { get; set; } = default!;

    /// <remarks>
    /// <see langword="null"/> on <c>/dashboard/events/new</c>; set on <c>/dashboard/events/{Id:guid}</c>.
    /// Deliberately a <see cref="Guid"/> route, not the slug the wireframe drew - keeps
    /// <see cref="ApiEventClient.GetEventAsync"/>'s existing <see cref="EventReadOutcome.Forbidden"/> path
    /// as the single source of "can't read this", rather than collapsing it into an empty-collection
    /// slug lookup.
    /// </remarks>
    [Parameter]
    public Guid? Id { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Admin, Director }

    private PageState state = PageState.Loading;
    private EventFormModel? model;
    private EditContext? editContext;
    private ValidationMessageStore? messageStore;
    private string? permissionLostMessage;
    private string? saveErrorMessage;
    private string? statusErrorMessage;
    private bool isSaving;
    private bool isDeleting;
    private bool isChangingStatus;

    private List<EventDirectorDto>? directorsForEvent;
    private List<UserRowDto>? candidateDirectors;
    private string? selectedDirectorUserId;
    private bool isAddingDirector;
    private bool isRemovingDirector;
    private string? directorErrorMessage;

    /// <remarks>
    /// Anchors the "why is this disabled" tooltip (ADR-0052) to the specific row hovered - a single shared
    /// field would only ever hold the last row's <see cref="ElementReference"/> once <see cref="LoadDirectorsAsync"/>
    /// finishes rendering every row in the loop, which is wrong for every row but the last. Keyed by
    /// <see cref="EventDirectorDto.GrantId"/> rather than index, so a reload that reorders the list can't
    /// point a tooltip at the wrong row.
    /// </remarks>
    private readonly Dictionary<Guid, ElementReference> adminGuardAnchors = [];

    private const string AdminGuardTooltipText =
        "Admins have access to every event. Remove them from the admin allowlist instead.";

    /// <remarks>
    /// Retained past <see cref="OnParametersSetAsync"/> for two reasons: the Director read-only view
    /// (<see cref="PageState.Director"/>) reads its <see cref="EventDto.StartsAt"/>/<see cref="EventDto.EndsAt"/>
    /// directly through <see cref="EventDateRange.FormatWithTime"/> on every render, so it updates for free
    /// once <see cref="viewerZone"/> resolves; and <see cref="OnAfterRenderAsync"/> re-derives
    /// <see cref="EventFormModel.StartsAtLocal"/>/<see cref="EventFormModel.EndsAtLocal"/> from it once the
    /// real zone is known, since they were first populated against the UTC fallback.
    /// </remarks>
    private EventDto? loadedDto;

    /// <remarks>See <c>Dashboard.razor.cs</c>'s identically-named field - same UTC-fallback-until-resolved shape.</remarks>
    private TimeZoneInfo viewerZone = TimeZoneInfo.Utc;

    /// <remarks>
    /// Three decisions worth naming here: (1) <c>Id is null</c> is a load-time gate, not a save-time
    /// failure - a non-Admin was never going to be allowed to create, so there's no form worth rendering.
    /// (2) A caught <see cref="EventDataUnavailableException"/> lands on <see cref="PageState.Unavailable"/>,
    /// not <see cref="PageState.Denied"/> - "the Event store didn't answer" versus "you can't read this
    /// Event" - since an unhandled exception here would otherwise crash the whole circuit (Blazor Server's
    /// <c>InteractiveServer</c> render mode has no equivalent of a failed HTTP request page). (3) Api's 403
    /// for an out-of-scope Event (ADR-0031) and "no such Event" are indistinguishable here by design; the
    /// <see cref="PageState.Denied"/> copy doesn't claim to know which.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        if (!authState.User.Claims.Any(c => c.Type == ClaimTypes.Role))
        {
            NavigationManager.NavigateTo("Account/NoAccess");
            return;
        }

        var accessView = new EventAccessView(authState.User);

        if (Id is null)
        {
            if (!accessView.CanEditEventDetails)
            {
                state = PageState.Denied;
                return;
            }

            model = new EventFormModel();
            BuildEditContext();
            state = PageState.Admin;
            return;
        }

        EventReadOutcome outcome;
        EventDto? dto;
        try
        {
            (outcome, dto) = await EventClient.GetEventAsync(Id.Value, CancellationToken.None);
        }
        catch (EventDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        if (outcome != EventReadOutcome.Success || dto is null)
        {
            state = PageState.Denied;
            return;
        }

        loadedDto = dto;
        model = new EventFormModel
        {
            Name = dto.Name,
            Slug = dto.Slug,
            Passcode = dto.Passcode,
            StartsAtLocal = ToLocalWallClock(dto.StartsAt),
            EndsAtLocal = ToLocalWallClock(dto.EndsAt)
        };

        if (accessView.CanEditEventDetails)
        {
            BuildEditContext();
            state = PageState.Admin;
            await LoadDirectorsAsync();
        }
        else
        {
            state = PageState.Director;
        }
    }

    private void BuildEditContext()
    {
        editContext = new EditContext(model!);
        messageStore = new ValidationMessageStore(editContext);
    }

    /// <remarks>
    /// Interop is only legal once the circuit has connected - <paramref name="firstRender"/> is the earliest
    /// safe point (matches <c>Dashboard.razor.cs</c>). <see cref="OnParametersSetAsync"/> already ran by
    /// then and populated <see cref="EventFormModel.StartsAtLocal"/>/<see cref="EventFormModel.EndsAtLocal"/>
    /// against the UTC fallback, so they're re-derived here from <see cref="loadedDto"/> once the real zone
    /// is known, then re-rendered - a signed-in Admin from a non-UTC zone would otherwise see a Start/End
    /// that's shifted from what's actually stored for the brief window before the circuit connects.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        viewerZone = await TimeZoneAccessor.GetTimeZoneAsync();

        if (model is not null && loadedDto is not null)
        {
            model.StartsAtLocal = ToLocalWallClock(loadedDto.StartsAt);
            model.EndsAtLocal = ToLocalWallClock(loadedDto.EndsAt);
        }

        StateHasChanged();
    }

    private DateTime? ToLocalWallClock(DateTimeOffset? utc) =>
        utc is null ? null : TimeZoneInfo.ConvertTime(utc.Value, viewerZone).DateTime;

    /// <remarks>
    /// The inverse of <see cref="ToLocalWallClock"/> - <paramref name="localWallClock"/> is what
    /// <c>InputDate</c> bound with <c>Type="InputDateType.DateTimeLocal"</c> hands back: a naive
    /// (<see cref="DateTimeKind.Unspecified"/>) clock reading with no timezone of its own, understood to be
    /// in <see cref="viewerZone"/> (the entering Admin's browser - CONTEXT.md's Starts at / Ends at entry).
    /// </remarks>
    /// <remarks>
    /// <see cref="EventDto"/> is a plain immutable class, not a record - no <c>with</c> expression available -
    /// so <see cref="GoLiveAsync"/>/<see cref="CancelEventAsync"/> rebuild it explicitly rather than re-fetching
    /// the whole Event over another round trip just to pick up the one field they already know changed.
    /// </remarks>
    private static EventDto WithStatus(EventDto dto, EventStatus status) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Slug = dto.Slug,
        Passcode = dto.Passcode,
        Status = status,
        StartsAt = dto.StartsAt,
        EndsAt = dto.EndsAt
    };

    private DateTimeOffset? ToUtc(DateTime? localWallClock)
    {
        if (localWallClock is not { } value)
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, viewerZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    /// <remarks>
    /// A caught <see cref="EventDataUnavailableException"/> is a transient Api/store outage, not a
    /// permission problem - unlike <see cref="permissionLostMessage"/>, the form stays enabled: the
    /// user's typed values are still good, retrying Save is the right next step, not signing in again.
    /// Uncaught, this would otherwise crash the circuit the same way <see cref="OnParametersSetAsync"/>'s
    /// read failure would.
    /// </remarks>
    private async Task SaveAsync()
    {
        isSaving = true;
        saveErrorMessage = null;
        messageStore?.Clear();
        editContext?.NotifyValidationStateChanged();

        try
        {
            if (Id is null)
            {
                await CreateAsync();
            }
            else
            {
                await UpdateAsync(Id.Value);
            }
        }
        catch (EventDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong saving this Event. Try again.";
        }

        isSaving = false;
    }

    /// <remarks>
    /// If a custom <see cref="EventFormModel.Slug"/>/<see cref="EventFormModel.Passcode"/> is supplied, or
    /// either date was typed, the Event already exists by the time the follow-up
    /// <see cref="ApiEventClient.UpdateAsync"/> runs - this method navigates to it regardless of how that
    /// PATCH goes, rather than reporting a failure that already half-happened. A conflict, invalid range, or
    /// transient failure there is left for the Admin to notice and fix from the edit page itself, since
    /// there's no field left to attach the error to once we've navigated away from this form. The follow-up
    /// PATCH must pass both dates even when neither was typed - <see cref="EventAttributesDto.StartsAt"/>/
    /// <see cref="EventAttributesDto.EndsAt"/> always serialize (ADR-0042), so omitting them here would send
    /// an explicit <c>null</c> and clear what the initial POST just set.
    /// </remarks>
    private async Task CreateAsync()
    {
        DateTimeOffset? startsAt = ToUtc(model!.StartsAtLocal);
        DateTimeOffset? endsAt = ToUtc(model.EndsAtLocal);

        (EventWriteOutcome outcome, EventDto? created, IReadOnlyList<string> pointers) =
            await EventClient.CreateAsync(model.Name ?? string.Empty, startsAt, endsAt, CancellationToken.None);

        if (outcome == EventWriteOutcome.Forbidden)
        {
            state = PageState.Denied;
            return;
        }

        if (TryApplyFieldErrors(outcome, pointers))
        {
            return;
        }

        string? slugOverride = NullIfBlank(model.Slug);
        string? passcodeOverride = NullIfBlank(model.Passcode);

        if (slugOverride is not null || passcodeOverride is not null)
        {
            try
            {
                await EventClient.UpdateAsync(
                    created!.Id, null, slugOverride, passcodeOverride, startsAt, endsAt, CancellationToken.None);
            }
            catch (EventDataUnavailableException)
            {
            }
        }

        NotificationService.Notify(NotificationSeverity.Success, "Event created");
        NavigationManager.NavigateTo($"dashboard/events/{created!.Id}");
    }

    /// <remarks>
    /// A <see cref="EventWriteOutcome.Forbidden"/> outcome here is claim-lag / mid-session demotion
    /// (<c>ApplicationUserClaimsPrincipalFactory</c>'s remarks) - distinct from <see cref="PageState.Denied"/>,
    /// which is about an out-of-scope Event, not a since-revoked Admin grant. The form stays rendered,
    /// disabled, with whatever the user had typed still visible. On success this navigates back to the
    /// list rather than staying put: staying on the same URL after a successful save, with only a toast
    /// to show for it, reads as the page having not responded at all. Matches <see cref="CreateAsync"/>'s
    /// own post-save navigation, just to the list instead of the new Event's own page.
    /// </remarks>
    private async Task UpdateAsync(Guid id)
    {
        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await EventClient.UpdateAsync(
            id, model!.Name, NullIfBlank(model.Slug), NullIfBlank(model.Passcode),
            ToUtc(model.StartsAtLocal), ToUtc(model.EndsAtLocal), CancellationToken.None);

        if (outcome == EventWriteOutcome.Forbidden)
        {
            permissionLostMessage = "You no longer have permission to edit this Event.";
            return;
        }

        if (TryApplyFieldErrors(outcome, pointers))
        {
            return;
        }

        NotificationService.Notify(NotificationSeverity.Success, "Changes saved");
        NavigateToDashboard();
    }

    /// <remarks>
    /// Not gated by <see cref="isSaving"/> or any unsaved-edit check - delete removes the whole record, so
    /// unsaved form edits become moot the moment it succeeds, same as clicking Cancel (grilled decision,
    /// P2-17). Uses <see cref="directorsForEvent"/>'s count already loaded by <see cref="LoadDirectorsAsync"/>
    /// for the confirm dialog's consequence list - no extra Api call needed here, unlike <c>Dashboard.razor.cs</c>'s
    /// grid row action, which has no Director data in hand yet. <see cref="EventWriteOutcome.Forbidden"/> reuses
    /// <see cref="permissionLostMessage"/>, matching <see cref="UpdateAsync"/>'s own claim-lag handling, rather
    /// than a delete-specific message.
    /// </remarks>
    private async Task DeleteAsync()
    {
        saveErrorMessage = null;

        int directorCount = directorsForEvent?.Count ?? 0;
        var parameters = EventDeleteConfirmation.BuildDialogParameters(model!.Name!, model.Slug!, directorCount);

        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Delete event?", parameters);
        if (confirmed is not true)
        {
            return;
        }

        isDeleting = true;

        try
        {
            EventWriteOutcome outcome = await EventClient.DeleteAsync(Id!.Value, CancellationToken.None);
            if (outcome is EventWriteOutcome.Success or EventWriteOutcome.NotFound)
            {
                NavigateToDashboard();
                return;
            }

            if (outcome == EventWriteOutcome.Forbidden)
            {
                permissionLostMessage = "You no longer have permission to delete this Event.";
            }
            else
            {
                saveErrorMessage = "Something went wrong deleting this Event. Try again.";
            }
        }
        catch (EventDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong deleting this Event. Try again.";
        }

        isDeleting = false;
    }

    /// <remarks>
    /// Its own dedicated status-only PATCH (<see cref="ApiEventClient.SetStatusAsync"/>), never folded into
    /// <see cref="SaveAsync"/>'s general Save changes flow - an Admin with unsaved edits in the form keeps
    /// them; going live doesn't submit or discard the form (grilled decision, P2-20). No confirm dialog -
    /// unlike Cancel event, publishing isn't destructive.
    /// </remarks>
    private Task GoLiveAsync() =>
        ChangeStatusAsync(EventStatus.Live, "Event is live", "Something went wrong going live. Try again.");

    /// <remarks>
    /// Mirrors <see cref="DeleteAsync"/>'s dialog-then-write shape and <see cref="GoLiveAsync"/>'s
    /// dedicated-PATCH-not-folded-into-Save discipline. Reuses <see cref="directorsForEvent"/>'s already-loaded
    /// count for the confirm dialog's consequence list, same as <see cref="DeleteAsync"/>.
    /// </remarks>
    private async Task CancelEventAsync()
    {
        var parameters = EventCancelConfirmation.BuildDialogParameters(model!.Name!, directorsForEvent?.Count);
        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Cancel this event?", parameters);
        if (confirmed is not true)
        {
            return;
        }

        await ChangeStatusAsync(
            EventStatus.Cancelled, "Event cancelled", "Something went wrong cancelling this Event. Try again.");
    }

    /// <remarks>
    /// Shared by <see cref="GoLiveAsync"/>/<see cref="CancelEventAsync"/> - both are a dedicated status-only
    /// PATCH with an identical outcome shape (success updates <see cref="loadedDto"/> and toasts; Forbidden is
    /// claim-lag, same message <see cref="UpdateAsync"/> and <see cref="DeleteAsync"/> already use; anything
    /// else, including a rejected transition, is a generic <see cref="statusErrorMessage"/>). Only the target
    /// <see cref="EventStatus"/> and the two success/failure strings differ between the two callers.
    /// </remarks>
    private async Task ChangeStatusAsync(EventStatus target, string successMessage, string unavailableMessage)
    {
        statusErrorMessage = null;
        isChangingStatus = true;

        try
        {
            EventWriteOutcome outcome = await EventClient.SetStatusAsync(Id!.Value, target, CancellationToken.None);
            if (outcome == EventWriteOutcome.Success)
            {
                loadedDto = WithStatus(loadedDto!, target);
                NotificationService.Notify(NotificationSeverity.Success, successMessage);
            }
            else if (outcome == EventWriteOutcome.Forbidden)
            {
                permissionLostMessage = "You no longer have permission to edit this Event.";
            }
            else
            {
                statusErrorMessage = "That change isn't allowed - refresh and try again.";
            }
        }
        catch (EventDataUnavailableException)
        {
            statusErrorMessage = unavailableMessage;
        }

        isChangingStatus = false;
    }

    /// <remarks>
    /// <c>forceLoad: true</c> - a plain <c>NavigationManager.NavigateTo("dashboard")</c> here reliably left
    /// the browser sitting on this same URL, form still fully populated, no error shown, no navigation at
    /// all (confirmed both by a live repro and by <c>EventManagementScenarios</c> failing this exact way).
    /// The difference from <see cref="CreateAsync"/>'s own in-circuit navigate (which works) is that this
    /// one crosses into a *different* component - <c>Dashboard.razor</c>, which still prerenders by default
    /// (ADR-0036), unlike this page. Blazor's client-side circuit router doesn't reliably complete an
    /// in-circuit navigation across that render-mode mismatch; <c>forceLoad</c> sidesteps it entirely with a
    /// real browser navigation instead of asking the existing circuit to swap components.
    /// </remarks>
    private void NavigateToDashboard() => NavigationManager.NavigateTo("dashboard", forceLoad: true);

    /// <remarks>
    /// Directors are added and removed from the Event, never the reverse (ADR-0035) - this is the only place
    /// in the app that writes an Event-scoped Grant, in either direction. <see cref="candidateDirectors"/>
    /// excludes anyone already in <see cref="directorsForEvent"/>, so the dropdown only ever offers a
    /// Director who isn't already assigned here - and a removal (P2-18, #113) reloads this list, so the
    /// removed person reappears in that dropdown immediately.
    /// </remarks>
    private async Task LoadDirectorsAsync()
    {
        directorErrorMessage = null;
        adminGuardAnchors.Clear();

        try
        {
            directorsForEvent = [.. await DirectorClient.GetDirectorsForEventAsync(Id!.Value, CancellationToken.None)];

            (IReadOnlyList<UserRowDto> allUsers, _) =
                await DirectorClient.GetUsersAsync(1, 1000, null, null, CancellationToken.None);

            var assignedIds = directorsForEvent.Select(director => director.UserId).ToHashSet();
            candidateDirectors = [.. allUsers.Where(u => u.IsDirector && !assignedIds.Contains(u.Id))];
        }
        catch (DirectorDataUnavailableException)
        {
            directorsForEvent = [];
            candidateDirectors = [];
            directorErrorMessage = "Something went wrong loading Directors. Try refreshing the page.";
        }
    }

    /// <remarks>
    /// Mirrors <see cref="DeleteAsync"/>'s dialog-then-write shape. <see cref="GrantWriteOutcome.Removed"/>
    /// and <see cref="GrantWriteOutcome.NotFound"/> both reload the list - see <see cref="GrantWriteOutcome.NotFound"/>'s
    /// remarks for why a stale target reads as success, not failure, matching <see cref="DeleteAsync"/>'s own
    /// <c>EventWriteOutcome.NotFound</c> handling. <see cref="GrantWriteOutcome.Forbidden"/> covers both
    /// claim-lag (a since-demoted Admin) and ADR-0051's target-holds-Admin guard - normally unreachable here
    /// since that row's button is disabled, but a page can go stale between render and click; both causes
    /// share the one message, since both resolve by refreshing. AC3's internal-JWT lag (a removed Director
    /// may keep read access until their session refreshes) is deliberately not surfaced here - it's already
    /// documented on <c>EventAccessPolicy</c>, and <see cref="DirectorRemovalConfirmation"/>'s remarks cover
    /// why it stays out of the confirm dialog too.
    /// </remarks>
    private async Task RemoveDirectorAsync(EventDirectorDto director)
    {
        directorErrorMessage = null;

        var parameters = DirectorRemovalConfirmation.BuildDialogParameters(director.DisplayLabel, model!.Name!);
        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Remove director?", parameters);
        if (confirmed is not true)
        {
            return;
        }

        isRemovingDirector = true;

        try
        {
            GrantWriteOutcome outcome = await DirectorClient.RemoveEventAccessAsync(director.GrantId, CancellationToken.None);

            if (outcome is GrantWriteOutcome.Removed or GrantWriteOutcome.NotFound)
            {
                await LoadDirectorsAsync();
            }
            else
            {
                directorErrorMessage = "Couldn't remove that Director - refresh and try again.";
            }
        }
        catch (DirectorDataUnavailableException)
        {
            directorErrorMessage = "Couldn't remove that Director - refresh and try again.";
        }

        isRemovingDirector = false;
    }

    private async Task AddDirectorAsync()
    {
        if (string.IsNullOrEmpty(selectedDirectorUserId))
        {
            return;
        }

        isAddingDirector = true;
        directorErrorMessage = null;

        try
        {
            GrantWriteOutcome outcome = await DirectorClient.GrantEventAccessAsync(
                selectedDirectorUserId, Id!.Value, CancellationToken.None);

            if (outcome is GrantWriteOutcome.Created or GrantWriteOutcome.AlreadyGranted)
            {
                selectedDirectorUserId = null;
                await LoadDirectorsAsync();
            }
            else
            {
                directorErrorMessage = "Couldn't add that Director - refresh and try again.";
            }
        }
        catch (DirectorDataUnavailableException)
        {
            directorErrorMessage = "Couldn't add that Director - refresh and try again.";
        }

        isAddingDirector = false;
    }

    /// <remarks>
    /// Shared by <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> - both route
    /// <see cref="EventWriteOutcome.Conflict"/>/<see cref="EventWriteOutcome.Invalid"/> onto
    /// <see cref="ApplyFieldErrors"/> identically, then stop; only their <see cref="EventWriteOutcome.Forbidden"/>
    /// handling differs, so that stays in each caller.
    /// </remarks>
    private bool TryApplyFieldErrors(EventWriteOutcome outcome, IReadOnlyList<string> pointers)
    {
        if (outcome is not (EventWriteOutcome.Conflict or EventWriteOutcome.Invalid))
        {
            return false;
        }

        ApplyFieldErrors(pointers);
        return true;
    }

    private void ApplyFieldErrors(IReadOnlyList<string> pointers)
    {
        if (messageStore is null || editContext is null)
        {
            return;
        }

        foreach (string pointer in pointers)
        {
            if (pointer.EndsWith("/name", StringComparison.Ordinal))
            {
                messageStore.Add(
                    new FieldIdentifier(model!, nameof(EventFormModel.Name)),
                    "This name is already in use by another Event.");
            }
            else if (pointer.EndsWith("/slug", StringComparison.Ordinal))
            {
                messageStore.Add(
                    new FieldIdentifier(model!, nameof(EventFormModel.Slug)),
                    "This address is already in use by another Event.");
            }
            else if (pointer.EndsWith("/startsAt", StringComparison.Ordinal))
            {
                messageStore.Add(
                    new FieldIdentifier(model!, nameof(EventFormModel.StartsAtLocal)),
                    "Set a start before setting an end.");
            }
            else if (pointer.EndsWith("/endsAt", StringComparison.Ordinal))
            {
                messageStore.Add(
                    new FieldIdentifier(model!, nameof(EventFormModel.EndsAtLocal)),
                    "End must be after the start.");
            }
        }

        editContext.NotifyValidationStateChanged();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <remarks>
    /// <see cref="Slug"/>/<see cref="Passcode"/> stay unvalidated beyond presence - format is enforced by
    /// Api's own CHECK constraint and uniqueness by its 409 (<see cref="ApplyFieldErrors"/>); duplicating
    /// that here isn't this story's job. <see cref="StartsAtLocal"/>/<see cref="EndsAtLocal"/> carry no
    /// DataAnnotations for the same reason, plus a second one: the page header's Save button calls
    /// <see cref="SaveAsync"/> directly rather than through <c>EditForm</c>'s <c>OnValidSubmit</c>, so a
    /// client-side validator wouldn't gate it anyway - Api's 422 (ADR-0042), surfaced through
    /// <see cref="ApplyFieldErrors"/>, is the only enforcement that actually runs.
    /// </remarks>
    private sealed class EventFormModel
    {
        [Required(ErrorMessage = "Enter a name.")]
        [StringLength(200)]
        public string? Name { get; set; }

        public string? Slug { get; set; }

        public string? Passcode { get; set; }

        /// <remarks>
        /// A naive wall-clock reading (<see cref="DateTimeKind.Unspecified"/>), in <c>viewerZone</c> - not a
        /// <see cref="DateTimeOffset"/> - because that's what <c>InputDate</c> bound with
        /// <c>Type="InputDateType.DateTimeLocal"</c> produces and consumes. Converted to/from UTC at the
        /// Api boundary by <see cref="ToUtc"/>/<see cref="ToLocalWallClock"/>.
        /// </remarks>
        public DateTime? StartsAtLocal { get; set; }

        public DateTime? EndsAtLocal { get; set; }
    }
}
