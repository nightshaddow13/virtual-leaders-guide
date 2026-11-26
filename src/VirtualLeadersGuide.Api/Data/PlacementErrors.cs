using System.Net;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Serialization.Objects;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// The errors Placement writes raise. Every conflict is a 409 carrying a stable <c>code</c> a client can map to
/// its own wording (ADR-0047's amendment) - Web holds a mirrored copy of these values, so don't rename one.
/// </summary>
public static class PlacementErrors
{
    /// <summary>The Activity is already placed at this exact path.</summary>
    public const string Duplicate = "placement-duplicate";

    /// <summary>An Activity was placed on a screen an InfoPage fills.</summary>
    public const string SlotHasInfoPage = "slot-has-infopage";

    /// <summary>An InfoPage was placed on a screen that shows Activities.</summary>
    public const string SlotHasActivities = "slot-has-activities";

    /// <summary>An InfoPage was placed on a screen another InfoPage fills.</summary>
    public const string SlotHasOtherInfoPage = "slot-has-other-infopage";

    /// <summary>The InfoPage already has its one Placement.</summary>
    public const string InfoPageAlreadyPlaced = "infopage-already-placed";

    /// <summary>A 409 with one of this type's codes.</summary>
    /// <param name="code">The stable error code.</param>
    /// <param name="detail">Human-readable explanation.</param>
    /// <returns>The exception to throw.</returns>
    public static JsonApiException Conflict(string code, string detail) =>
        new(new ErrorObject(HttpStatusCode.Conflict)
        {
            Code = code,
            Title = "Resource conflict.",
            Detail = detail,
            Source = new ErrorSource { Pointer = "/data" }
        });

    /// <summary>A 422 pointing at the offending member.</summary>
    /// <param name="title">Short summary.</param>
    /// <param name="detail">Human-readable explanation.</param>
    /// <param name="pointer">JSON pointer to the member.</param>
    /// <returns>The exception to throw.</returns>
    public static JsonApiException Unprocessable(string title, string detail, string pointer) =>
        new(new ErrorObject(HttpStatusCode.UnprocessableEntity)
        {
            Title = title,
            Detail = detail,
            Source = new ErrorSource { Pointer = pointer }
        });
}
