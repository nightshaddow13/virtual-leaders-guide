namespace VirtualLeadersGuide.Web.Events;

/// <summary>
/// Thrown when a call to Api's <c>/api/events</c> resource fails at the transport level, or returns a
/// status <see cref="ApiEventClient"/> doesn't otherwise handle.
/// </summary>
/// <remarks>
/// Mirrors <c>Authorization.AuthorizationDataUnavailableException</c>'s role for the role-grant path -
/// deliberately not swallowed into an empty result, so a caller fails loudly instead of rendering as if
/// the Event store returned nothing.
/// </remarks>
public sealed class EventDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
