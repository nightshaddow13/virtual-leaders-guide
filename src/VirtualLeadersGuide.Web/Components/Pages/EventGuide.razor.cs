using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Markdown;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// An Event's public gated guide - wireframes 1c (locked splash, P4-2 #72) and 1g (wrong-passcode error, P4-2
/// #72), and, once unlocked, its InfoPages (P4-1, #23) as a flat, Title-sorted list with no Tab navigation -
/// P5-14 (#88) replaces this wholesale once InfoPage Placement and Activities exist.
/// </summary>
/// <remarks>
/// Static SSR, same reasoning as <c>Home.razor</c> - the passcode form POSTs back here, and a
/// <c>RadzenButton</c>'s <c>Click</c> would be inert with no circuit (ADR-0034), so every dead-end state
/// uses a real <c>&lt;a&gt;</c>, styled like a Radzen button the way <c>SiteHeader</c>'s own real-anchor nav
/// links already do, rather than <c>RadzenButton</c>. <c>EventGuide.razor.css</c>'s banner strip is
/// token-built, no photo asset (matches <c>Home.razor.css</c>'s own "token-built for now" note - #138
/// tracks sourcing real imagery for the whole public surface).
/// </remarks>
public partial class EventGuide
{
    [Inject]
    private PublicEventClient EventClient { get; set; } = default!;

    [Inject]
    private PasscodeUnlockCookie UnlockCookie { get; set; } = default!;

    [Inject]
    private MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string Slug { get; set; } = "";

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <remarks>
    /// Populated regardless of render mode (unlike an interactive page's own circuit-scoped state) - static
    /// SSR still cascades <see cref="AuthenticationState"/> from the sign-in cookie, which is all
    /// <see cref="IsStaffForThisEventAsync"/> needs.
    /// </remarks>
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    private enum PageState { Loading, NotFound, Cancelled, Locked, Unlocked, Unavailable }

    private PageState state = PageState.Loading;
    private PublicEventDto? loadedEvent;
    private string? passcodeErrorMessage;
    private IReadOnlyList<PublicInfoPageDto> infoPages = [];
    private string? contentErrorMessage;

    /// <remarks>
    /// No browser timezone is available to convert into (<see cref="Time.BrowserTimeZoneAccessor"/> needs a
    /// connected interactive circuit's JS interop, which this deliberately static-SSR page never has,
    /// ADR-0034) - the date line renders in UTC rather than the visitor's own zone. A day-level range can be
    /// off by one day right at a UTC boundary; accepted for this story rather than adding circuit/interop
    /// machinery to a page whose whole point is staying circuit-free.
    /// </remarks>
    private string DateRangeText => EventDateRange.Format(
        loadedEvent?.StartsAt, loadedEvent?.EndsAt, TimeZoneInfo.Utc, DateTimeOffset.UtcNow);

    protected override async Task OnParametersSetAsync()
    {
        Input ??= new();
        await LoadAsync();
    }

    /// <remarks>
    /// Resolution order, per the plan: not found (unknown Slug, or a <see cref="EventStatus.Draft"/> Event -
    /// indistinguishable by design) short-circuits first; <see cref="EventStatus.Cancelled"/> next, and
    /// *unconditionally* - staff included, never bypassed - since the AC's "goes dark" is a fact about the
    /// Event, not about who's asking (confirmed with the user; contrast with the staff bypass below, which
    /// exists purely for convenience). Past both of those, a signed-in Admin/Director's own access
    /// (<see cref="IsStaffForThisEventAsync"/>) reaches <see cref="PageState.Unlocked"/> with no cookie
    /// involved - checked first, short-circuiting <see cref="PasscodeUnlockCookie.IsUnlocked"/> for staff, so
    /// staff and an already-unlocked visitor land on the exact same state and see the exact same InfoPages
    /// (<see cref="LoadInfoPagesAsync"/>, P4-1 #23).
    /// <para>
    /// Deliberate gap, confirmed with the user: this lookup is the same anonymous endpoint for every caller,
    /// staff included, so a <see cref="EventStatus.Draft"/> Event reads as <see cref="PageState.NotFound"/>
    /// even for the Admin who owns it - the staff bypass is only ever reachable once an Event is
    /// Live/Past/Cancelled, so there is still no way to preview a Draft Event's InfoPages before publishing
    /// (tracked as a follow-up, #196, not closed by this story).
    /// </para>
    /// </remarks>
    private async Task LoadAsync()
    {
        try
        {
            (PublicEventLookupOutcome outcome, PublicEventDto? @event) =
                await EventClient.GetBySlugAsync(Slug, CancellationToken.None);

            if (outcome != PublicEventLookupOutcome.Success || @event is null)
            {
                state = PageState.NotFound;
                return;
            }

            loadedEvent = @event;

            if (Enum.Parse<EventStatus>(@event.Status) == EventStatus.Cancelled)
            {
                state = PageState.Cancelled;
                return;
            }

            bool unlocked = await IsStaffForThisEventAsync(@event.Id)
                || UnlockCookie.IsUnlocked(HttpContext, @event.Id, @event.PasscodeVersion);

            if (!unlocked)
            {
                state = PageState.Locked;
                return;
            }

            state = PageState.Unlocked;
            await LoadInfoPagesAsync();
        }
        catch (PublicGuideUnavailableException)
        {
            state = PageState.Unavailable;
        }
    }

    /// <remarks>
    /// A separate <c>try</c>/<c>catch</c> from <see cref="LoadAsync"/>'s own, deliberately (ADR-0068's plan,
    /// grilled decision): a visitor who already unlocked the guide keeps that state on a failed InfoPages
    /// fetch, seeing an inline error where the content would be, rather than being dropped onto the full-page
    /// <see cref="PageState.Unavailable"/> dead-end - that state is reserved for the Event lookup itself never
    /// responding, a different failure than content not loading after the gate already succeeded.
    /// </remarks>
    private async Task LoadInfoPagesAsync()
    {
        try
        {
            infoPages = await EventClient.GetInfoPagesAsync(Slug, CancellationToken.None);
        }
        catch (PublicGuideUnavailableException)
        {
            contentErrorMessage = "This guide's info pages didn't load. Try refreshing the page.";
        }
    }

    /// <remarks>
    /// A rendering-only check (<see cref="EventAccessView"/>'s own remarks) built from the sign-in cookie's
    /// claims - already generalizes to a future Event-scoped role (e.g. a logged-in Participant, #44) with
    /// no change here, since <see cref="EventAccessView.CanReadEvent"/> is what would grow a branch, not this
    /// call site.
    /// </remarks>
    private async Task<bool> IsStaffForThisEventAsync(Guid eventId)
    {
        if (AuthenticationStateTask is null)
        {
            return false;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        if (!authState.User.Claims.Any(c => c.Type == ClaimTypes.Role))
        {
            return false;
        }

        return new EventAccessView(authState.User).CanReadEvent(eventId);
    }

    /// <remarks>
    /// Keeps the Locked form visible on every failure - wrong guess or transport error alike - rather than
    /// swapping to a dead-end panel, matching <c>Home.razor.cs.FindEventAsync</c>'s same reasoning (ADR-0034:
    /// no working button to put on one anyway under static SSR).
    /// </remarks>
    private async Task UnlockAsync()
    {
        passcodeErrorMessage = null;

        if (loadedEvent is null)
        {
            return;
        }

        try
        {
            (PasscodeCheckOutcome outcome, Guid? eventId, int? passcodeVersion) =
                await EventClient.CheckPasscodeAsync(Slug, Input.Passcode, CancellationToken.None);

            if (outcome != PasscodeCheckOutcome.Matched || eventId is null || passcodeVersion is null)
            {
                passcodeErrorMessage =
                    "That passcode doesn't match this event. Check the leader packet - two words, no space.";
                return;
            }

            UnlockCookie.Unlock(HttpContext, eventId.Value, passcodeVersion.Value, loadedEvent.EndsAt, loadedEvent.StartsAt);
            state = PageState.Unlocked;
        }
        catch (PublicGuideUnavailableException)
        {
            passcodeErrorMessage = "Something went wrong checking that passcode. Try again.";
        }
    }

    private sealed class InputModel
    {
        [Required(ErrorMessage = "Enter the passcode.")]
        public string Passcode { get; set; } = "";
    }
}
