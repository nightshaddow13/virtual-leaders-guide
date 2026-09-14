using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class Home
{
    [Inject]
    private PublicEventClient EventClient { get; set; } = default!;

    [Inject]
    private PasscodeUnlockCookie UnlockCookie { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    private string? errorMessage;

    protected override void OnInitialized()
    {
        Input ??= new();
    }

    /// <remarks>
    /// Five outcomes, per the plan's table: not found (unknown address, or a <see cref="EventStatus.Draft"/>
    /// Event - indistinguishable by design, <c>PublicGuideEndpoints</c>'s remarks) shows an inline error and
    /// stays here; <see cref="EventStatus.Cancelled"/> redirects straight to <c>/e/{slug}</c> without a
    /// passcode check at all - the cancelled state lives there, and it applies "correct passcode or not"
    /// (ADR-0044), so checking here would be pointless; a matched passcode on <see cref="EventStatus.Live"/>/
    /// <see cref="EventStatus.Past"/> unlocks and redirects; a wrong one shows an inline error and stays. A
    /// transport failure also shows an inline error and stays, rather than swapping to a dead-end panel - the
    /// form (and whatever the visitor already typed, preserved via <see cref="SupplyParameterFromFormAttribute"/>)
    /// is still the right thing to show, and static SSR gives no working "Try again" button to put on a
    /// separate panel anyway (ADR-0034: <c>RadzenButton</c>'s <c>Click</c> is inert with no circuit).
    /// Deliberately renders its error where the visitor submitted, not on <c>/e/{slug}</c> the way wireframe
    /// 1g draws a wrong-passcode error - bouncing a <c>/</c> submission to another URL just to show 1g's copy
    /// would need a query-string error flag and lose the address the visitor already typed.
    /// </remarks>
    private async Task FindEventAsync()
    {
        errorMessage = null;

        string slug = NormalizeAddress(Input.Address);

        try
        {
            (PublicEventLookupOutcome lookupOutcome, PublicEventDto? @event) =
                await EventClient.GetBySlugAsync(slug, CancellationToken.None);

            if (lookupOutcome != PublicEventLookupOutcome.Success || @event is null)
            {
                errorMessage = "We couldn't find an event at that address.";
                return;
            }

            if (Enum.Parse<EventStatus>(@event.Status) == EventStatus.Cancelled)
            {
                NavigationManager.NavigateTo($"e/{@event.Slug}");
                return;
            }

            (PasscodeCheckOutcome checkOutcome, Guid? eventId, int? passcodeVersion) =
                await EventClient.CheckPasscodeAsync(slug, Input.Passcode, CancellationToken.None);

            if (checkOutcome != PasscodeCheckOutcome.Matched || eventId is null || passcodeVersion is null)
            {
                errorMessage = "That passcode doesn't match this event. Check the leader packet - two words, no space.";
                return;
            }

            UnlockCookie.Unlock(HttpContext, eventId.Value, passcodeVersion.Value, @event.EndsAt, @event.StartsAt);
            NavigationManager.NavigateTo($"e/{@event.Slug}");
        }
        catch (PublicGuideUnavailableException)
        {
            errorMessage = "Something went wrong looking up that event. Try again.";
        }
    }

    /// <remarks>
    /// Accepts whatever a visitor pastes: a bare Slug (<c>summer-camporee</c>), one with a leading
    /// <c>/e/</c> (matching the field's own visual prefix), or a full URL copied from an address bar
    /// (<c>https://vlg.org/e/summer-camporee</c>) - all three should reach the same Event. Api's own
    /// lookup already lowercases (<c>Event.Slug</c>'s setter), but normalizing here too means an
    /// obviously-wrong address never round-trips to Api at all.
    /// </remarks>
    private static string NormalizeAddress(string? address)
    {
        string value = (address ?? string.Empty).Trim();

        int prefixIndex = value.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
        if (prefixIndex >= 0)
        {
            value = value[(prefixIndex + 3)..];
        }

        return value.Trim('/').ToLowerInvariant();
    }

    private sealed class InputModel
    {
        [Required(ErrorMessage = "Enter the event address.")]
        public string Address { get; set; } = "";

        [Required(ErrorMessage = "Enter the passcode.")]
        public string Passcode { get; set; } = "";
    }
}
