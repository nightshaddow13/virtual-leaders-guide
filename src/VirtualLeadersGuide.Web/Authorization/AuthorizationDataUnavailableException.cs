namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>
/// Thrown when a call to Api's internal authorization endpoints fails at the transport level, or returns a
/// status <see cref="ApiRoleGrantClient"/> doesn't otherwise handle.
/// </summary>
/// <remarks>
/// Deliberately not swallowed into an empty grant list — see ADR-0022's Consequences for why (same
/// reasoning as <see cref="VirtualLeadersGuide.Web.Identity.IdentityStoreUnavailableException"/>, applied to
/// the authorization data path).
/// </remarks>
public sealed class AuthorizationDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
