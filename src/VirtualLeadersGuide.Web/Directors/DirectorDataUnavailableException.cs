namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Thrown when a call to Api's <c>/api/users</c> or <c>/api/roleGrants</c> resource fails at the transport
/// level, or returns a status <see cref="ApiDirectorClient"/> doesn't otherwise handle.
/// </summary>
/// <remarks>Mirrors <c>Events.EventDataUnavailableException</c>'s role for the Users/Directors screen (P2-12, #43).</remarks>
public sealed class DirectorDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
