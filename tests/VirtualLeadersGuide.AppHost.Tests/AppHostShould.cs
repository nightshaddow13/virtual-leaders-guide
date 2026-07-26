using Microsoft.Extensions.Logging;

namespace VirtualLeadersGuide.AppHost.Tests;

public class AppHostShould
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // NOTE: AppHost.cs currently registers zero resources (Web/Api/SQL land in
    // P1-3/P1-4/P1-5). Because there is no named resource yet, this test cannot
    // literally assert "at least one resource reaches Healthy" per the issue's
    // acceptance criteria - instead it asserts the DistributedApplication
    // builds, starts, and stops cleanly with no unhandled exceptions. Tracked
    // as a follow-up in P1-9 (#28), which will extend this test to call
    // app.ResourceNotifications.WaitForResourceHealthyAsync(<resource name>, ...)
    // once P1-3/P1-4 register a real resource.
    [Fact]
    public async Task BuildAndStartSuccessfully_WhenNoResourcesAreRegistered_ForStartAsync()
    {
        var cancellationToken = CancellationToken.None;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.VirtualLeadersGuide_AppHost>(cancellationToken);

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
