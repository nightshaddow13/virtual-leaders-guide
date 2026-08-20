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
    /// <remarks>
    /// Cold CI has to pull the mssql/azurite images before SQL Server's first-boot init even starts
    /// (<c>AppHostShould</c> observed ~40s locally with a warm cache) - this covers container start, both
    /// resource waits, and both readiness probes below, with headroom on top.
    /// </remarks>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan ProbePollInterval = TimeSpan.FromSeconds(1);

    /// <remarks>
    /// Test-only values, mirroring <c>ApiWebApplicationFactory</c>'s constants in <c>Api.Tests</c> - any
    /// string works locally, it just has to be what this fixture also hands the AppHost via
    /// <see cref="FixedTestArgs"/>. <see cref="InternalJwtKey"/> must be at least 32 bytes (HMAC-SHA256's
    /// minimum key size).
    /// </remarks>
    private const string InternalApiKey = "e2e-test-internal-api-key";
    private const string InternalJwtKey = "e2e-test-internal-jwt-signing-key-at-least-32-bytes-long";

    /// <remarks>
    /// Web's own <c>InternalApiKeyHandler.cs</c> and <c>Tools.SeedUser</c> both hardcode this literal too,
    /// rather than referencing Api's <c>InternalApiKeyDefaults</c> across a process boundary - this fixture
    /// follows the same precedent instead of adding a <c>ProjectReference</c> to Api just for one string.
    /// </remarks>
    private const string InternalApiKeyHeaderName = "X-Internal-Key";

    /// <remarks>
    /// <see cref="InternalApiKey"/>/<see cref="InternalJwtKey"/> (P2-5, ADR-0007) and
    /// <c>acs-connection-string</c> (P2-1) all have no default value (fail-closed, see
    /// <c>AppHostShould</c>), so every AppHost testing builder must supply them explicitly. Unlike
    /// <c>AppHostShould</c>, this fixture actually waits for api/web to become healthy, so omitting
    /// <see cref="InternalJwtKey"/> here would leave both resources unable to start.
    /// <see cref="AdminAllowlistedEmail"/> is built per-instance in <see cref="InitializeAsync"/> instead -
    /// it needs a value generated at runtime, unlike the other three fixed test-only values here.
    /// </remarks>
    private static readonly string[] FixedTestArgs =
    [
        $"Parameters:internal-api-key={InternalApiKey}",
        $"Parameters:internal-jwt-key={InternalJwtKey}",
        "Parameters:acs-connection-string=test-only-value"
    ];

    private DistributedApplication _app = null!;
    private HttpClient _probeClient = null!;
    private HttpClient _identityApiHttpClient = null!;

    /// <summary>
    /// The <c>web</c> resource's HTTPS base URL, populated once <see cref="InitializeAsync"/> has proven both
    /// <c>api</c> and <c>web</c> actually serve requests. <c>Web</c> unconditionally redirects HTTP to HTTPS
    /// (<c>Program.cs</c>'s <c>UseHttpsRedirection</c>), so this always points at the HTTPS endpoint - run
    /// <c>dotnet dev-certs https --trust</c> first, or every navigation against this URL fails on a cert error.
    /// </summary>
    public Uri WebBaseUrl { get; private set; } = null!;

    /// <summary>
    /// Seeds and mutates local-Identity accounts directly against <c>api</c>, for tests that need a real
    /// account in place before driving the rendered Login form.
    /// </summary>
    public IdentityApiClient IdentityApi { get; private set; } = null!;

    /// <summary>
    /// A single email, generated fresh for this run and passed to the AppHost's <c>admin-allowlist</c>
    /// parameter (P2-4, #13; ADR-0008), for the one test that needs to sign in as an allowlisted Admin.
    /// </summary>
    /// <remarks>
    /// Run-scoped rather than a fixed literal: the SQL container's data volume persists across runs (see this
    /// fixture's own <see cref="BuildFailureMessage"/>), so a fixed email would 409 on <c>CreateUserAsync</c>
    /// the second time this suite runs against the same volume.
    /// </remarks>
    public string AdminAllowlistedEmail { get; } = $"e2e-admin-{Guid.NewGuid():n}@example.test";

    /// <summary>
    /// Where <c>web</c> is told to write every email it sends (P2.1-4, #62; ADR-0032), wired onto the <c>web</c>
    /// resource in <see cref="InitializeAsync"/>. <c>Email:FileSinkAllowed</c> needs no equivalent wiring here -
    /// <c>AppHost.cs</c> already grants it whenever <c>!IsPublishMode</c>, which is true under this fixture.
    /// </summary>
    public EmailFileSink EmailSink { get; } = new();

    /// <inheritdoc/>
    /// <remarks>
    /// Waits for <c>api</c> before <c>web</c>: <c>AppHost.cs</c>'s <c>web</c> resource does not
    /// <c>WaitFor(api)</c>, so waiting on <c>web</c> alone proves nothing about <c>api</c>. Neither resource
    /// declares a <c>HealthCheckAnnotation</c>, so <c>WaitForResourceHealthyAsync</c> only proves "reached
    /// Running," not "serving requests" - that's what <see cref="WaitForApiReadyAsync"/> and
    /// <see cref="WaitForWebReadyAsync"/> are for. <c>StopOnResourceUnavailable</c> makes a
    /// <c>FailedToStart</c> resource fail the wait instead of hanging on it forever (the default,
    /// <c>WaitOnResourceUnavailable</c>, waits indefinitely for a restart that will never come here). The
    /// probe <see cref="HttpClient"/> carries no client-level <c>Timeout</c> -
    /// <see cref="PollUntilAsync"/> caps each individual attempt itself, so a slow (not merely refused)
    /// attempt is retried rather than failing the whole probe outright. <see cref="IdentityApi"/> is built
    /// only once <c>api</c> has proven it's actually serving requests, so tests that seed a user before
    /// their first navigation don't race <c>api</c>'s own startup.
    /// </remarks>
    public async Task InitializeAsync()
    {
        CancellationToken cancellationToken = CancellationToken.None;

        string[] testArgs = [.. FixedTestArgs, $"Parameters:admin-allowlist={AdminAllowlistedEmail}"];

        try
        {
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.VirtualLeadersGuide_AppHost>(testArgs, cancellationToken);

            ConfigureWebEmailSink(appHost);

            _app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
            await _app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

            await _app.ResourceNotifications
                .WaitForResourceHealthyAsync("api", WaitBehavior.StopOnResourceUnavailable, cancellationToken)
                .WaitAsync(DefaultTimeout, cancellationToken);
            await _app.ResourceNotifications
                .WaitForResourceHealthyAsync("web", WaitBehavior.StopOnResourceUnavailable, cancellationToken)
                .WaitAsync(DefaultTimeout, cancellationToken);

            _probeClient = new HttpClient();

            await WaitForApiReadyAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
            await WaitForWebReadyAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

            _identityApiHttpClient = new HttpClient { BaseAddress = _app.GetEndpoint("api", "http") };
            _identityApiHttpClient.DefaultRequestHeaders.Add(InternalApiKeyHeaderName, InternalApiKey);
            IdentityApi = new IdentityApiClient(_identityApiHttpClient);
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
        _identityApiHttpClient?.Dispose();
        EmailSink.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <remarks>
    /// The only step between <c>CreateAsync</c> and <c>BuildAsync</c> in this fixture - every other
    /// configuration value goes through <see cref="FixedTestArgs"/> as a <c>Parameters:*</c> argument instead.
    /// <c>Email:Provider</c>/<c>Email:FileSinkDirectory</c> aren't Aspire parameters, though - they're plain
    /// environment variables on the <c>web</c> resource, so they need <c>CreateResourceBuilder</c> rather than
    /// a testArgs entry.
    /// </remarks>
    private void ConfigureWebEmailSink(IDistributedApplicationTestingBuilder appHost)
    {
        ProjectResource webResource = appHost.Resources.OfType<ProjectResource>().Single(r => r.Name == "web");

        appHost.CreateResourceBuilder(webResource)
            .WithEnvironment("Email__Provider", "FileSink")
            .WithEnvironment("Email__FileSinkDirectory", EmailSink.Directory);
    }

    /// <remarks>
    /// "Running"/"healthy" only proves the api process started, not that it's actually serving requests yet
    /// (see <see cref="InitializeAsync"/>). Polling a real endpoint that depends on routing, the
    /// <c>X-Internal-Key</c> auth handler, and a migrated schema all being live simultaneously is what the
    /// issue's acceptance criteria asks for - a 404 here (this user genuinely doesn't exist) is success, not
    /// a probe failure.
    /// </remarks>
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

    /// <remarks>
    /// Polling before the first Playwright navigation avoids eating Playwright's default navigation timeout
    /// on cold JIT (the issue's own rationale for this probe).
    /// </remarks>
    private async Task WaitForWebReadyAsync(CancellationToken cancellationToken)
    {
        WebBaseUrl = _app.GetEndpoint("web", "https");
        var probeUri = new Uri(WebBaseUrl, "Account/Login");

        await PollUntilAsync(probeUri, HttpStatusCode.OK, static _ => { }, cancellationToken);
    }

    /// <remarks>
    /// Each attempt gets its own short deadline, linked to the outer <paramref name="cancellationToken"/>
    /// (the phase's overall <see cref="DefaultTimeout"/> budget from <see cref="InitializeAsync"/>). A
    /// single slow attempt - a hung TCP handshake, a cold-start response that just needs another second -
    /// must not fail the whole probe; only the outer deadline expiring should. Distinguishing the two by
    /// exception type alone doesn't work here: an <see cref="HttpClient"/> timeout surfaces as a
    /// <see cref="TaskCanceledException"/> wrapping a <see cref="TimeoutException"/> wrapping
    /// <see cref="System.IO.IOException"/>/<see cref="System.Net.Sockets.SocketException"/>, not a clean
    /// <see cref="HttpRequestException"/>. Any exception while the outer deadline hasn't been reached -
    /// refused connection, hung handshake, slow cold-start response, or a not-yet-migrated schema - means
    /// "not ready yet," so polling continues; once the deadline is reached the exception rethrows (via the
    /// <see langword="when"/> guard failing) so <see cref="InitializeAsync"/>'s catch blocks can build a
    /// real failure message instead of looping forever.
    /// </remarks>
    private async Task PollUntilAsync(
        Uri uri,
        HttpStatusCode expectedStatusCode,
        Action<HttpRequestMessage> configureRequest,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
