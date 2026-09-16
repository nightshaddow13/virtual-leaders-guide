namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// Thrown when a call to Api's <c>/api/infoPages</c> resource fails at the transport level, or returns a
/// status <see cref="ApiInfoPageClient"/> doesn't otherwise handle.
/// </summary>
/// <remarks>
/// Mirrors <c>Events.EventDataUnavailableException</c>'s role for the Event path - deliberately not
/// swallowed into an empty result, so a caller fails loudly instead of rendering as if the InfoPage store
/// returned nothing.
/// </remarks>
public sealed class InfoPageDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
