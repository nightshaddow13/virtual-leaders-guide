using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Registers the <see cref="IEmailSender{TUser}"/> named by <c>Email:Provider</c> - <see cref="AcsEmailSender"/>
/// (default) or <see cref="FileSinkEmailSender"/> - as a fail-closed config fork (P2.1-4, #62; ADR-0032).
/// </summary>
public static class EmailSenderRegistration
{
    private const string ProviderKey = "Email:Provider";

    private const string FileSinkAllowedKey = "Email:FileSinkAllowed";

    private const string AcsProvider = "Acs";

    private const string FileSinkProvider = "FileSink";

    /// <remarks>
    /// <c>Email:FileSinkAllowed</c> is set only by <c>AppHost.cs</c>'s <c>!builder.ExecutionContext.IsPublishMode</c>
    /// block, which is absent from a published deploy manifest by construction (ADR-0013 uses the same guard
    /// for <c>Migrations:ApplyAutomatically</c>) - so <c>Email:Provider=FileSink</c> can reach a deployed
    /// environment only if someone hand-edits that environment's config directly, and this still refuses it
    /// there. An unrecognized <c>Email:Provider</c> value also throws, rather than silently falling back to
    /// <see cref="AcsEmailSender"/> - a typo in the E2E fixture's config would otherwise surface as a hung test
    /// waiting on an email that was never going to arrive.
    /// </remarks>
    public static void AddConfiguredEmailSender(this WebApplicationBuilder builder)
    {
        var provider = builder.Configuration[ProviderKey];

        if (string.IsNullOrEmpty(provider) || string.Equals(provider, AcsProvider, StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddScoped<IEmailSender<ApplicationUser>, AcsEmailSender>();
            return;
        }

        if (string.Equals(provider, FileSinkProvider, StringComparison.OrdinalIgnoreCase))
        {
            if (!builder.Configuration.GetValue<bool>(FileSinkAllowedKey))
            {
                throw new InvalidOperationException(
                    $"{ProviderKey}={FileSinkProvider} is not allowed here - {FileSinkAllowedKey} is not set.");
            }

            builder.Services.AddScoped<IEmailSender<ApplicationUser>, FileSinkEmailSender>();
            return;
        }

        throw new InvalidOperationException($"{ProviderKey} has an unrecognized value: '{provider}'.");
    }
}
