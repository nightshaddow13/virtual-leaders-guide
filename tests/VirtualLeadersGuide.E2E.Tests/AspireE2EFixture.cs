using Aspire.Hosting;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Boots the real Aspire-orchestrated stack (<c>Api</c>, <c>Web</c>, a real SQL Server container, and
/// Azurite) once for the entire E2E test run, and exposes <see cref="WebBaseUrl"/> only once both resources
/// are proven to actually serve requests - not merely "started" (see <see cref="InitializeAsync"/>).
/// </summary>
/// <remarks>
/// Shared across the whole run via <see cref="AspireE2ECollection"/> - see ADR-0025's Consequences for why
/// every E2E test class must join that exact collection rather than declaring its own.
/// </remarks>
public sealed class AspireE2EFixture : IAsyncLifetime
{
    // Cold CI has to pull the mssql/azurite images before SQL Server's first-boot init even starts
    // (AppHostShould.cs observed ~40s locally with a warm cache) - this covers container start, both resource
    // waits, and both readiness probes below, with headroom on top.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan ProbePollInterval = TimeSpan.FromSeconds(1);

    // Test-only values, mirroring ApiWebApplicationFactory's constants in Api.Tests - any string works
    // locally, it just has to be what this fixture also hands the AppHost via TestArgs below.
    // internal-jwt-key must be >= 32 bytes (HMAC-SHA256's minimum key size).
    private const string InternalApiKey = "e2e-test-internal-api-key";
    private const string InternalJwtKey = "e2e-test-internal-jwt-signing-key-at-least-32-bytes-long";

    // Web's own InternalApiKeyHandler.cs and Tools.SeedUser both hardcode this literal too, rather than
    // referencing Api's InternalApiKeyDefaults across a process boundary - this fixture follows the same
    // precedent instead of adding a ProjectReference to Api just for one string.
    private const string InternalApiKeyHeaderName = "X-Internal-Key";

    // internal-api-key and internal-jwt-key (P2-5, ADR-0007) and acs-connection-string (P2-1) all have no
    // default value (fail-closed, see AppHostShould.cs), so every AppHost testing builder must supply them
    // explicitly. Unlike AppHostShould.cs, this fixture actually waits for api/web to become healthy, so
    // omitting internal-jwt-key here would leave both resources unable to start.
    private static readonly string[] TestArgs =
    [
        $"Parameters:internal-api-key={InternalApiKey}",
        $"Parameters:internal-jwt-key={InternalJwtKey}",
        "Parameters:acs-connection-string=test-only-value"
    ];

    private DistributedApplication _app = null!;
    private HttpClient _probeClient = null!;

    /// <summary>
    /// The <c>web</c> resource's HTTPS base URL, populated once <see cref="InitializeAsync"/> has proven both
    /// <c>api</c> and <c>web</c> actually serve requests. <c>Web</c> unconditionally redirects HTTP to HTTPS
    /// (<c>Program.cs</c>'s <c>UseHttpsRedirection</c>), so this always points at the HTTPS endpoint - run
    /// <c>dotnet dev-certs https --trust</c> first, or every navigation against this URL fails on a cert error.
    /// </summary>
    public Uri WebBaseUrl { get; private set; } = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        CancellationToken cancellationToken = CancellationToken.None;

        try
        {
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.VirtualLeadersGuide_AppHost>(TestArgs, cancellationToken);

            _app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
            await _app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

            // api first: AppHost.cs's web resource does not WaitFor(api), so waiting on web alone proves
            // nothing about api. Neither resource declares a HealthCheckAnnotation, so
            // WaitForResourceHealthyAsync only proves "reached Running", not "serving requests" - that's what
            // the two readiness probes below are for. StopOnResourceUnavailable makes a FailedToStart
            // resource fail this wait instead of hanging on it forever (the default,
            // WaitOnResourceUnavailable, waits indefinitely for a restart that will never come here).
            await _app.ResourceNotifications
                .WaitForResourceHealthyAsync("api", WaitBehavior.StopOnResourceUnavailable, cancellationToken)
                .WaitAsync(DefaultTimeout, cancellationToken);
            await _app.ResourceNotifications
                .WaitForResourceHealthyAsync("web", WaitBehavior.StopOnResourceUnavailable, cancellationToken)
                .WaitAsync(DefaultTimeout, cancellationToken);

            // No client-level Timeout here - PollUntilAsync caps each individual attempt itself, so a slow
            // (not merely refused) attempt is retried rather than failing the whole probe outright.
            _probeClient = new HttpClient();

            await WaitForApiReadyAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
            await WaitForWebReadyAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                BuildFailureMessage("timed out waiting for the stack to become ready"), ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(BuildFailureMessage(ex.Message), ex);
        }
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        _probeClient?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    // "Running"/"healthy" only proves the api process started, not that it's actually serving requests yet
    // (see InitializeAsync). Polling a real endpoint that depends on routing, the X-Internal-Key auth
    // handler, and a migrated schema all being live simultaneously is what the issue's acceptance criteria
    // asks for - a 404 here (this user genuinely doesn't exist) is success, not a probe failure.
    private async Task WaitForApiReadyAsync(CancellationToken cancellationToken)
    {
        Uri apiBaseUrl = _app.GetEndpoint("api", "http");
        var probeUri = new Uri(apiBaseUrl, InternalIdentityRoutes.ForUserByNormalizedEmail("NOBODY@EXAMPLE.TEST"));

        await PollUntilAsync(
            probeUri,
            HttpStatusCode.NotFound,
            request => request.Headers.Add(InternalApiKeyHeaderName, InternalApiKey),
            cancellationToken);
    }

    // Polling before the first Playwright navigation avoids eating Playwright's default navigation timeout on
    // cold JIT (the issue's own rationale for this probe).
    private async Task WaitForWebReadyAsync(CancellationToken cancellationToken)
    {
        WebBaseUrl = _app.GetEndpoint("web", "https");
        var probeUri = new Uri(WebBaseUrl, "Account/Login");

        await PollUntilAsync(probeUri, HttpStatusCode.OK, static _ => { }, cancellationToken);
    }

    private async Task PollUntilAsync(
        Uri uri,
        HttpStatusCode expectedStatusCode,
        Action<HttpRequestMessage> configureRequest,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each attempt gets its own short deadline, linked to the outer cancellationToken (the phase's
            // overall DefaultTimeout budget from InitializeAsync). A single slow attempt - a hung TCP
            // handshake, a cold-start response that just needs another second - must not fail the whole
            // probe; only the outer deadline expiring should. Distinguishing the two by exception type alone
            // doesn't work here: a HttpClient timeout surfaces as TaskCanceledException wrapping a
            // TimeoutException wrapping IOException/SocketException, not a clean HttpRequestException.
            using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptDeadline.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                configureRequest(request);

                using HttpResponseMessage response = await _probeClient.SendAsync(request, attemptDeadline.Token);
                if (response.StatusCode == expectedStatusCode)
                {
                    return;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Not ready yet - refused connection, hung handshake, slow cold-start response, or a
                // not-yet-migrated schema all land here. Keep polling as long as the outer deadline hasn't
                // been reached; rethrow (via the `when` guard failing) once it has, so InitializeAsync's
                // catch blocks can build a real failure message instead of looping forever.
            }

            await Task.Delay(ProbePollInterval, cancellationToken);
        }
    }

    private static string BuildFailureMessage(string reason) =>
        $"""
        AspireE2EFixture failed to bring the stack up ready: {reason}

        Before running this suite, confirm:
          - Docker Desktop is running.
          - `dotnet dev-certs https --trust` has been run at least once on this machine.
          - No other AppHost is running - this suite cannot run alongside `dotnet run --project
            src/VirtualLeadersGuide.AppHost` or `VirtualLeadersGuide.AppHost.Tests`, since all three share the
            same fixed launch-profile ports and the same persistent SQL Server data volume.
        """;
}
