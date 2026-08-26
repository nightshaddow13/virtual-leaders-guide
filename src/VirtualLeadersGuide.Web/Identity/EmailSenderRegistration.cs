using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Registers the <see cref="IEmailSender{TUser}"/> (and <see cref="IInviteEmailSender"/>, P2-12/#43) named
/// by <c>Email:Provider</c> - <see cref="AcsEmailSender"/> (default) or <see cref="FileSinkEmailSender"/> -
/// as a fail-closed config fork (P2.1-4, #62; ADR-0032).
/// </summary>
public static class EmailSenderRegistration
{
    private const string ProviderKey = "Email:Provider";

    private const string FileSinkAllowedKey = "Email:FileSinkAllowed";

    private const string AcsProvider = "Acs";

    private const string FileSinkProvider = "FileSink";

    /// <summary>
    /// Registers <see cref="AcsEmailSender"/> or <see cref="FileSinkEmailSender"/> depending on <c>Email:Provider</c>.
    /// </summary>
    /// <remarks>See ADR-0032 for the fail-closed guard this enforces and why.</remarks>
    public static void AddConfiguredEmailSender(this WebApplicationBuilder builder)
    {
        var provider = builder.Configuration[ProviderKey];

        if (string.IsNullOrEmpty(provider) || string.Equals(provider, AcsProvider, StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddScoped<AcsEmailSender>();
            builder.Services.AddScoped<IEmailSender<ApplicationUser>>(sp => sp.GetRequiredService<AcsEmailSender>());
            builder.Services.AddScoped<IInviteEmailSender>(sp => sp.GetRequiredService<AcsEmailSender>());
            return;
        }

        if (string.Equals(provider, FileSinkProvider, StringComparison.OrdinalIgnoreCase))
        {
            if (!builder.Configuration.GetValue<bool>(FileSinkAllowedKey))
            {
                throw new InvalidOperationException(
                    $"{ProviderKey}={FileSinkProvider} is not allowed here - {FileSinkAllowedKey} is not set.");
            }

            if (builder.Environment.IsProduction())
            {
                throw new InvalidOperationException(
                    $"{ProviderKey}={FileSinkProvider} is not allowed under the Production environment.");
            }

            builder.Services.AddScoped<FileSinkEmailSender>();
            builder.Services.AddScoped<IEmailSender<ApplicationUser>>(sp => sp.GetRequiredService<FileSinkEmailSender>());
            builder.Services.AddScoped<IInviteEmailSender>(sp => sp.GetRequiredService<FileSinkEmailSender>());
            return;
        }

        throw new InvalidOperationException($"{ProviderKey} has an unrecognized value: '{provider}'.");
    }
}
