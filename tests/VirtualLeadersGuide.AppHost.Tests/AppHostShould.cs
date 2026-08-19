using Microsoft.Extensions.Logging;

namespace VirtualLeadersGuide.AppHost.Tests;

/// <remarks>
/// Asserts the <c>DistributedApplication</c> builds, starts, and stops cleanly with no unhandled exceptions,
/// and that a core Aspire service resolves from its DI container - not that any specific resource reaches
/// Healthy. AppHost.cs now registers real resources (SQL/api/web, P1-3/P1-4/P1-5); stronger per-resource
/// health checks for api/web live in <c>AspireE2EFixture</c> (<c>WaitForResourceHealthyAsync</c>), not here.
/// </remarks>
public class AppHostShould
{
    /// <remarks>
    /// 30s wasn't enough on a GitHub-hosted CI runner: a cold VM has no cached mssql/azurite images, so the
    /// pull alone can eat most of that before SQL Server's first-boot init (~40s observed locally even with
    /// a warm image cache) even starts. 90s leaves headroom for both, per phase.
    /// </remarks>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    /// <remarks>
    /// <c>internal-api-key</c> (P1-7, ADR-0015) and <c>acs-connection-string</c> (P2-1) both have no default
    /// value (fail-closed) so every AppHost testing builder must supply them explicitly - real environments
    /// get them from user-secrets or a Container Apps secret, but tests need their own throwaway values.
    /// </remarks>
    private static readonly string[] TestArgs =
    [
        "Parameters:internal-api-key=test-only-value",
        "Parameters:acs-connection-string=test-only-value"
    ];

    [Fact]
    public async Task BuildAndStartSuccessfully_WhenResourcesAreRegistered_ForStartAsync()
    {
        var cancellationToken = CancellationToken.None;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.VirtualLeadersGuide_AppHost>(TestArgs, cancellationToken);

        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });

        await using var app = await appHost.BuildAsync(cancellationToken)
            .WaitAsync(DefaultTimeout, cancellationToken);

        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var resourceNotifications = app.Services.GetRequiredService<ResourceNotificationService>();
        Assert.NotNull(resourceNotifications);

        await app.StopAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
    }
}
