namespace VirtualLeadersGuide.Web.Authorization;

// Thrown by ApiRoleGrantClient when a call to Api's internal authorization endpoints fails at the
// transport level (retries exhausted via the "Api" HttpClient's standard resilience handler - see
// ServiceDefaults/Extensions.cs - or Api unreachable outright), or returns a status the client doesn't
// otherwise handle. Deliberately NOT swallowed into an empty grant list: a role-claim-minting caller
// (P2-5, #14) reading this as "the user holds no grants" on a genuine Api outage would silently sign
// someone in with no access instead of surfacing the failure - mirrors IdentityStoreUnavailableException's
// same reasoning (see ADR-0022's Consequences) for the authorization data path.
public sealed class AuthorizationDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
