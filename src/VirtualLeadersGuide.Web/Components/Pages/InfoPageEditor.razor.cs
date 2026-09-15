using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.InfoPages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class InfoPageEditor
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiInfoPageClient InfoPageClient { get; set; } = default!;

    [Inject]
    private MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Parameter]
    public Guid EventId { get; set; }

    /// <remarks><see langword="null"/> on <c>.../info-pages/new</c>; set on <c>.../info-pages/{InfoPageId:guid}</c> - same shape as <c>EventEditor.Id</c>.</remarks>
    [Parameter]
    public Guid? InfoPageId { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    /// <remarks>Drives the responsive Write/Preview pane switch (grilled decision) - see <c>InfoPageEditor.razor.css</c>. Only meaningful below the 64rem breakpoint; ignored above it, where both panes always show.</remarks>
    private enum PaneView { Write, Preview }

    private PageState state = PageState.Loading;
    private PaneView activePane = PaneView.Write;
    private string? eventName;
    private bool isAdmin;
    private InfoPageFormModel? model;
    private string? saveErrorMessage;
    private string? permissionLostMessage;
    private bool isSaving;
    private bool isDeleting;

    /// <remarks>
    /// Gates on <see cref="ApiEventClient.GetEventAsync"/> first, exactly like <c>InfoPageList.razor.cs</c> -
    /// same reasoning (ADR-0059's collection-filter narrowing can't itself distinguish "not assigned" from
    /// "nothing here yet", but a single-resource InfoPage read still needs its own check since the Event
    /// check alone doesn't confirm this specific InfoPage id belongs to this Event or that it exists). When
    /// editing, a loaded InfoPage whose <see cref="InfoPageDto.EventId"/> doesn't match the route's
    /// <see cref="EventId"/> (a stale or hand-edited URL) falls into <see cref="PageState.Denied"/> too,
    /// rather than a new state - the copy doesn't distinguish why.
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

        isAdmin = new EventAccessView(authState.User).IsAdmin;

        EventReadOutcome eventOutcome;
        EventDto? eventDto;
        try
        {
            (eventOutcome, eventDto) = await EventClient.GetEventAsync(EventId, CancellationToken.None);
        }
        catch (EventDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        if (eventOutcome != EventReadOutcome.Success || eventDto is null)
        {
            state = PageState.Denied;
            return;
        }

        eventName = eventDto.Name;

        if (InfoPageId is null)
        {
            model = new InfoPageFormModel();
            state = PageState.Ready;
            return;
        }

        InfoPageReadOutcome infoPageOutcome;
        InfoPageDto? infoPageDto;
        try
        {
            (infoPageOutcome, infoPageDto) = await InfoPageClient.GetInfoPageAsync(InfoPageId.Value, CancellationToken.None);
        }
        catch (InfoPageDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        if (infoPageOutcome != InfoPageReadOutcome.Success || infoPageDto is null || infoPageDto.EventId != EventId)
        {
            state = PageState.Denied;
            return;
        }

        model = new InfoPageFormModel { Title = infoPageDto.Title, MarkdownContent = infoPageDto.MarkdownContent };
        state = PageState.Ready;
    }

    /// <remarks>
    /// Wired to <c>EditForm</c>'s <c>OnValidSubmit</c>, not called directly from a header button
    /// (deviates from <c>EventEditor.razor.cs</c>'s pattern deliberately - see <c>InfoPageEditor.razor</c>'s
    /// remarks) - <see cref="InfoPageFormModel.Title"/>'s <see cref="StringLengthAttribute"/> exists
    /// specifically to stop an over-length Title from ever reaching Api (which would 500, not 422, on this
    /// one field), so submission has to actually be gated on it, not just display a message beside an
    /// unblocked Save.
    /// </remarks>
    private async Task SaveAsync()
    {
        isSaving = true;
        saveErrorMessage = null;

        try
        {
            if (InfoPageId is null)
            {
                await CreateAsync();
            }
            else
            {
                await UpdateAsync(InfoPageId.Value);
            }
        }
        catch (InfoPageDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong saving this Info page. Try again.";
        }

        isSaving = false;
    }

    private async Task CreateAsync()
    {
        (InfoPageWriteOutcome outcome, InfoPageDto? created, IReadOnlyList<string> pointers) = await InfoPageClient.CreateAsync(
            EventId, model!.Title ?? string.Empty, model.MarkdownContent ?? string.Empty, CancellationToken.None);

        if (outcome == InfoPageWriteOutcome.Forbidden)
        {
            state = PageState.Denied;
            return;
        }

        if (outcome == InfoPageWriteOutcome.Invalid)
        {
            // Only reachable via the unknown-Event pointer, which normal navigation can't trigger since
            // EventId always comes from an already-verified route - no form field to attach this to.
            saveErrorMessage = pointers.Count > 0
                ? "This Event no longer exists. Try again from the dashboard."
                : "Something went wrong saving this Info page. Try again.";
            return;
        }

        NotificationService.Notify(NotificationSeverity.Success, "Info page created");
        NavigationManager.NavigateTo($"dashboard/events/{EventId}/info-pages/{created!.Id}");
    }

    /// <remarks>
    /// <see cref="InfoPageWriteOutcome.Forbidden"/> here is claim-lag - a Director removed from the Event
    /// mid-session - not an out-of-scope navigation, matching <c>EventEditor.razor.cs</c>'s
    /// <c>UpdateAsync</c>/<c>permissionLostMessage</c> distinction from <see cref="PageState.Denied"/>.
    /// </remarks>
    private async Task UpdateAsync(Guid id)
    {
        InfoPageWriteOutcome outcome = await InfoPageClient.UpdateAsync(
            id, model!.Title ?? string.Empty, model.MarkdownContent ?? string.Empty, CancellationToken.None);

        if (outcome == InfoPageWriteOutcome.Forbidden)
        {
            permissionLostMessage = "You no longer have permission to edit this Info page.";
            return;
        }

        NotificationService.Notify(NotificationSeverity.Success, "Changes saved");
        NavigateToList();
    }

    private async Task DeleteAsync()
    {
        saveErrorMessage = null;

        var parameters = InfoPageDeleteConfirmation.BuildDialogParameters(model!.Title ?? "this page");
        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Delete info page?", parameters);
        if (confirmed is not true)
        {
            return;
        }

        isDeleting = true;

        try
        {
            InfoPageWriteOutcome outcome = await InfoPageClient.DeleteAsync(InfoPageId!.Value, CancellationToken.None);
            if (outcome is InfoPageWriteOutcome.Success or InfoPageWriteOutcome.NotFound)
            {
                NavigateToList();
                return;
            }

            if (outcome == InfoPageWriteOutcome.Forbidden)
            {
                permissionLostMessage = "You no longer have permission to delete this Info page.";
            }
        }
        catch (InfoPageDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong deleting this Info page. Try again.";
        }

        isDeleting = false;
    }

    /// <remarks><c>forceLoad: true</c> - same reasoning as <c>EventEditor.razor.cs</c>'s <c>NavigateToDashboard</c>: crossing from this <c>prerender: false</c> page to another needs a real browser navigation, not an in-circuit one.</remarks>
    private void NavigateToList() => NavigationManager.NavigateTo($"dashboard/events/{EventId}/info-pages", forceLoad: true);

    private sealed class InfoPageFormModel
    {
        [Required(ErrorMessage = "Enter a title.")]
        [StringLength(200, ErrorMessage = "Title can't be longer than 200 characters.")]
        public string? Title { get; set; }

        /// <remarks>No <see cref="RequiredAttribute"/> - empty content is legal at the Api layer (CONTEXT.md's InfoPage entry: "written but not yet placed" is a normal state, not a draft one).</remarks>
        public string? MarkdownContent { get; set; }
    }
}
