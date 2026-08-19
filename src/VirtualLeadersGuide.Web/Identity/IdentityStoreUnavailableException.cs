namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Thrown when a call to Api's internal identity endpoints fails at the transport level — resilience-handler
/// retries exhausted, or Api unreachable outright.
/// </summary>
/// <remarks>
/// Deliberately not swallowed into a null/"not found" result; see ADR-0022's Consequences for why.
/// Propagates out of <see cref="UserManager{TUser}"/> to <c>Program.cs</c>'s <c>UseExceptionHandler("/Error")</c>.
/// </remarks>
public sealed class IdentityStoreUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
