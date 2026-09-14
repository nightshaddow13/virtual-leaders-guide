namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>
/// Thrown when a call to Api's <c>/internal/public/*</c> surface fails at the transport level, or returns a
/// status <see cref="PublicEventClient"/> doesn't otherwise handle.
/// </summary>
/// <remarks>
/// Mirrors <c>Events.EventDataUnavailableException</c>'s role for the Event-resource path - deliberately not
/// swallowed into an empty result, so a caller fails loudly instead of rendering as if no Event existed.
/// </remarks>
public sealed class PublicGuideUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
