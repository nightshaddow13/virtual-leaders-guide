using Microsoft.Extensions.Logging;

namespace VirtualLeadersGuide.AppHost.Tests;

public class AppHostShould
{
    // 30s wasn't enough on a GitHub-hosted CI runner: a cold VM has no cached
    // mssql/azurite images, so the pull alone can eat most of that before SQL
    // Server's first-boot init (~40s observed locally even with a warm image
    // cache) even starts. 90s leaves headroom for both, per phase.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    // NOTE: AppHost.cs currently registers zero resources (Web/Api/SQL land in
    // P1-3/P1-4/P1-5). Because there is no named resource yet, this test cannot
    // literally assert "at least one resource reaches Healthy" per the issue's
    // acceptance criteria - instead it asserts the DistributedApplication
    // builds, starts, and stops cleanly with no unhandled exceptions. Tracked
    // as a follow-up in P1-9 (#28), which will extend this test to call
    // app.ResourceNotifications.WaitForResourceHealthyAsync(<resource name>, ...)
    // once P1-3/P1-4 register a real resource.
    // internal-api-key (P1-7, ADR-0015) and acs-connection-string (P2-1) both have no default value
    // (fail-closed) so every AppHost testing builder must supply them explicitly - real environments get them
    // from user-secrets or a Container Apps secret, but tests need their own throwaway values.
    private static readonly string[] TestArgs =
    [
        "Parameters:internal-api-key=test-only-value",
        "Parameters:acs-connection-string=test-only-value"
    ];

    [Fact]
    public async Task BuildAndStartSuccessfully_WhenNoResourcesAreRegistered_ForStartAsync()
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

        // Resolve a core Aspire service off the running app to confirm the
        // DI container came up correctly, not just that StartAsync returned.
        var resourceNotifications = app.Services.GetRequiredService<ResourceNotificationService>();
        Assert.NotNull(resourceNotifications);

        await app.StopAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
    }
}
