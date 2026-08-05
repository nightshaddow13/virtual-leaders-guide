namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>
/// Binds the <c>AdminAllowlist</c> configuration section - the config-driven list of emails ADR-0008's
/// per-login sync promotes to platform-wide Admin (see <see cref="AdminAllowlistSynchronizer"/>).
/// </summary>
/// <remarks>
/// <see cref="Emails"/> is a single delimited string, not a <c>string[]</c> - this is the repo's first
/// <c>IOptions&lt;T&gt;</c> binding, and production config is set by hand via
/// <c>az containerapp update --set-env-vars</c> (there is no AppHost in the deployed environment). A JSON
/// array would need indexed env-var keys (<c>AdminAllowlist__Emails__0</c>, <c>__1</c>, ...) with no ergonomic
/// way to add or remove a single entry from that step - a delimited string is one variable, editable in place.
/// </remarks>
public sealed class AdminAllowlistOptions
{
    /// <summary>The configuration section name this type binds to.</summary>
    public const string SectionName = "AdminAllowlist";

    /// <summary>
    /// A <c>;</c>- or <c>,</c>-delimited list of emails to promote to platform-wide Admin on sign-in. Empty or
    /// unset means no Admins - ADR-0008 deliberately allows this (no "last Admin" protection).
    /// </summary>
    public string Emails { get; set; } = string.Empty;
}
