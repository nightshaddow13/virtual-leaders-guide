namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// Thrown when a call to Api's <c>/api/activities</c> resource fails at the transport level, or returns a
/// status <see cref="ApiActivityClient"/> doesn't otherwise handle.
/// </summary>
/// <remarks>
/// Mirrors <c>InfoPages.InfoPageDataUnavailableException</c>'s role for the InfoPage path - deliberately not
/// swallowed into an empty result, so a caller fails loudly instead of rendering as if the Activity store
/// returned nothing.
/// </remarks>
public sealed class ActivityDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
