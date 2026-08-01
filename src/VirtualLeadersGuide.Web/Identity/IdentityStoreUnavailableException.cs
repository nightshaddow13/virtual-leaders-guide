namespace VirtualLeadersGuide.Web.Identity;

// Thrown by ApiUserStore when a call to Api's internal identity endpoints fails at the transport level
// (retries exhausted via the "Api" HttpClient's standard resilience handler - see
// ServiceDefaults/Extensions.cs - or Api unreachable outright). Deliberately NOT swallowed into a null/
// "not found" result: SignInManager.PasswordSignInAsync treats a null FindByEmailAsync result as an
// ordinary failed login (by design, so it doesn't reveal whether an email exists), so a genuine store
// outage must not silently read as "your password is wrong." This propagates out of UserManager to
// Program.cs's existing app.UseExceptionHandler("/Error") instead. See ADR-0022's Consequences.
public sealed class IdentityStoreUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
