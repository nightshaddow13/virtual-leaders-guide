using System.Reflection;
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

    /// <remarks>
    /// Not per-run - a sweep error is rare enough, and useful enough across runs, that appending to one fixed
    /// file beats inventing a second timestamped-folder scheme alongside <c>E2ETestBase</c>'s own <c>RunRoot</c>
    /// (which this class has no access to - it's private to that type, and computed independently).
    /// </remarks>
    private static readonly string SweepLogDirectory = ResolveArtifactRoot();

    private DistributedApplication _app = null!;
    private HttpClient _probeClient = null!;
    private HttpClient _identityApiHttpClient = null!;
    private HttpClient _eventsApiHttpClient = null!;
    private HttpClient _usersApiHttpClient = null!;

    /// <summary>
    /// Whether <c>VLG_E2E_KEEP_DATA=1</c> is set - disables per-test cleanup (<see cref="E2ETestBase"/>) and
    /// this fixture's own run-end sweep, so a developer can inspect a real post-run database instead of only
    /// a <c>trace.zip</c> (ADR-0039). Fixture seeding (<see cref="SeedFixtureDataAsync"/>) always runs
    /// regardless - it's idempotent by necessity anyway (an ordinary, non-kept-data run must survive finding
    /// its fixture accounts already seeded from the run before it), so gating it behind this flag would only
    /// add a second code path without preventing anything the flag is actually for.
    /// </summary>
    public static bool KeepData { get; } = Environment.GetEnvironmentVariable("VLG_E2E_KEEP_DATA") == "1";

    /// <summary>The fixture Admin's fixed email (ADR-0039) - see <see cref="AdminAllowlistedEmail"/>.</summary>
    public const string AdminEmail = "e2e-admin@example.test";

    /// <summary>The fixture Director's fixed email - unscoped Director Role plus a grant on <see cref="RetainedEventName"/> (ADR-0039).</summary>
    public const string DirectorEmail = "e2e-director@example.test";

    /// <summary>The fixture no-role account's fixed email - no grants at all (ADR-0039).</summary>
    public const string NoRoleEmail = "e2e-norole@example.test";

    /// <summary>The fixture pending-Invite account's fixed email - unscoped Director Role, no password, no grant (ADR-0039).</summary>
    public const string InvitedEmail = "e2e-invited@example.test";

    /// <summary>
    /// The one Event retained across every run (ADR-0039) - never guid-suffixed, unlike every Event a test
    /// creates via <see cref="E2ETestBase.CreateEventAsync"/>, which is how <see cref="EnsureRetainedEventAsync"/>
    /// finds it again idempotently on a later run.
    /// </summary>
    public const string RetainedEventName = "e2e-retained-event";

    /// <summary>
    /// The <see cref="RetainedEventName"/> Event's id, populated by <see cref="InitializeAsync"/>'s fixture
    /// seeding - for a test that wants to assert against, or ignore, the one Event guaranteed to already
    /// exist.
    /// </summary>
    public Guid RetainedEventId { get; private set; }

    private string _directorUserId = null!;
    private string _invitedUserId = null!;

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
    /// Creates, deletes, and lists Events directly against <c>api</c>'s <c>/api/events</c> resource - backs
    /// <see cref="E2ETestBase"/>'s tracked cleanup and this fixture's own fixture seeding/run-end sweep
    /// (ADR-0039).
    /// </summary>
    public EventsApiClient Events { get; private set; } = null!;

    /// <summary>Lists Users by email directly against <c>api</c>'s <c>/api/users</c> resource - backs this fixture's own run-end sweep (ADR-0039).</summary>
    public UsersApiClient Users { get; private set; } = null!;

    /// <summary>
    /// The fixture Admin's fixed email (<see cref="AdminEmail"/>), passed to the AppHost's
    /// <c>admin-allowlist</c> parameter (P2-4, #13; ADR-0008), for every test that needs to sign in as an
    /// allowlisted Admin.
    /// </summary>
    /// <remarks>
    /// Fixed rather than run-scoped, unlike before ADR-0039: the SQL container's data volume persists across
    /// runs (see this fixture's own <see cref="BuildFailureMessage"/>), which used to mean a fixed email
    /// would 409 on <c>CreateUserAsync</c> the second time this suite ran against the same volume. Now that
    /// fixture accounts are seeded idempotently (<see cref="EnsureUserAsync"/>) and nothing else in this run
    /// creates or deletes this specific account, a fixed literal is exactly what a shared fixture identity
    /// should be.
    /// </remarks>
    public string AdminAllowlistedEmail { get; } = AdminEmail;

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

            _eventsApiHttpClient = new HttpClient { BaseAddress = _app.GetEndpoint("api", "http") };
            _eventsApiHttpClient.DefaultRequestHeaders.Add(InternalApiKeyHeaderName, InternalApiKey);
            Events = new EventsApiClient(_eventsApiHttpClient, InternalJwtKey);

            _usersApiHttpClient = new HttpClient { BaseAddress = _app.GetEndpoint("api", "http") };
            _usersApiHttpClient.DefaultRequestHeaders.Add(InternalApiKeyHeaderName, InternalApiKey);
            Users = new UsersApiClient(_usersApiHttpClient, InternalJwtKey);

            await SeedFixtureDataAsync(cancellationToken);
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
    /// <remarks>
    /// Two-phase teardown, both while <c>api</c> is still reachable, before anything else disposes (ADR-0039).
    /// <see cref="SweepOrphanedDataAsync"/> runs first and is best-effort/logged only, matching ADR-0028's
    /// existing per-test teardown discipline - a backstop for a crashed or killed test's own tracked cleanup
    /// never running. <see cref="VerifyRetentionAsync"/> runs second and is deliberately allowed to throw:
    /// unlike a per-test <c>DisposeAsync</c> failure (which ADR-0028 protects from corrupting that test's own
    /// already-decided result), a collection-fixture <c>DisposeAsync</c> exception isn't attached to any one
    /// test - xUnit surfaces it as its own distinct run-level error. That's what makes ADR-0039's "nothing
    /// else survives" rule an enforced fact rather than an aspiration a log file quietly ignores. An earlier
    /// draft of this design planned a dedicated <c>[Fact]</c> for this instead, ordered to run last - dropped
    /// because xUnit v2 doesn't actually guarantee execution order across test *classes* sharing one
    /// collection (only that they run sequentially, not in what order), which a same-collection <c>[Fact]</c>
    /// can't rely on. This fixture's own <c>DisposeAsync</c> has the one ordering guarantee that's real: it
    /// runs after every test in the collection has finished, unconditionally.
    /// </remarks>
    public async Task DisposeAsync()
    {
        if (!KeepData && _app is not null)
        {
            await SweepOrphanedDataAsync();
            await VerifyRetentionAsync();
        }

        _probeClient?.Dispose();
        _identityApiHttpClient?.Dispose();
        _eventsApiHttpClient?.Dispose();
        _usersApiHttpClient?.Dispose();
        EmailSink.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Asserts the exact ADR-0039 retention counts directly against the database, after
    /// <see cref="SweepOrphanedDataAsync"/> has had its chance to clean up. Deliberately not wrapped in a
    /// try/catch the way that sweep is - see <see cref="DisposeAsync"/>'s own remarks for why letting this
    /// throw is both safe and the entire point.
    /// </summary>
    /// <exception cref="InvalidOperationException">The retained data does not match ADR-0039's table exactly.</exception>
    private async Task VerifyRetentionAsync()
    {
        var cancellationToken = CancellationToken.None;

        IReadOnlyList<(Guid Id, string Name)> events = await Events.ListE2EEventsAsync(cancellationToken);
        if (events.Count != 1 || events[0].Name != RetainedEventName)
        {
            throw new InvalidOperationException(
                $"ADR-0039 violation: expected exactly 1 e2e- Event (the retained '{RetainedEventName}'), " +
                $"found {events.Count}: [{string.Join(", ", events.Select(e => e.Name))}].");
        }

        string[] expectedEmails = [AdminEmail, DirectorEmail, NoRoleEmail, InvitedEmail];
        IReadOnlyList<string> actualEmails = await Users.ListExampleTestEmailsAsync(cancellationToken);
        if (actualEmails.Count != expectedEmails.Length
            || !expectedEmails.All(email => actualEmails.Contains(email, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"ADR-0039 violation: expected exactly the {expectedEmails.Length} fixture accounts, found " +
                $"{actualEmails.Count}: [{string.Join(", ", actualEmails)}].");
        }

        int directorGrantCount = (await IdentityApi.GetGrantsAsync(_directorUserId, cancellationToken)).Count;
        int invitedGrantCount = (await IdentityApi.GetGrantsAsync(_invitedUserId, cancellationToken)).Count;
        int totalGrantCount = directorGrantCount + invitedGrantCount;
        if (totalGrantCount != 3)
        {
            throw new InvalidOperationException(
                "ADR-0039 violation: expected exactly 3 Role grants across the fixture Director (unscoped + " +
                $"event-scoped) and the fixture Invite (unscoped), found {totalGrantCount} " +
                $"({directorGrantCount} + {invitedGrantCount}).");
        }
    }

    /// <summary>
    /// Idempotently seeds the four ADR-0039 fixture accounts and the one retained Event, and every grant
    /// their table entries imply, tolerating a run that finds them already in place from a prior one.
    /// </summary>
    /// <remarks>
    /// Runs unconditionally, including under <see cref="KeepData"/> - see that property's own remarks for
    /// why gating this behind the flag would add a code path without preventing anything.
    /// </remarks>
    private async Task SeedFixtureDataAsync(CancellationToken cancellationToken)
    {
        if (!await IdentityApi.ExistsAsync(AdminEmail, cancellationToken))
        {
            await IdentityApi.CreateUserAsync(AdminEmail, TestCredentials.KnownPassword, cancellationToken);
        }

        IdentityUserDto director = await EnsureUserAsync(DirectorEmail, cancellationToken);
        await EnsureUserAsync(NoRoleEmail, cancellationToken);
        IdentityUserDto invited = await EnsureInviteAsync(InvitedEmail, cancellationToken);
        _directorUserId = director.Id;
        _invitedUserId = invited.Id;

        RetainedEventId = await EnsureRetainedEventAsync(cancellationToken);

        await EnsureUnscopedDirectorGrantAsync(director.Id, cancellationToken);
        await EnsureUnscopedDirectorGrantAsync(invited.Id, cancellationToken);
        await EnsureEventGrantAsync(director.Id, RetainedEventId, cancellationToken);
    }

    private async Task<IdentityUserDto> EnsureUserAsync(string email, CancellationToken cancellationToken) =>
        await IdentityApi.TryGetByEmailAsync(email, cancellationToken)
        ?? await IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, cancellationToken);

    private async Task<IdentityUserDto> EnsureInviteAsync(string email, CancellationToken cancellationToken) =>
        await IdentityApi.TryGetByEmailAsync(email, cancellationToken)
        ?? await IdentityApi.CreateInviteAsync(email, cancellationToken);

    private async Task EnsureUnscopedDirectorGrantAsync(string userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<RoleGrantDto> grants = await IdentityApi.GetGrantsAsync(userId, cancellationToken);
        if (!grants.Any(grant => grant.RoleId == RoleIds.Director && grant.EventId is null))
        {
            await IdentityApi.GrantDirectorUnscopedAsync(userId, cancellationToken);
        }
    }

    private async Task EnsureEventGrantAsync(string userId, Guid eventId, CancellationToken cancellationToken)
    {
        IReadOnlyList<RoleGrantDto> grants = await IdentityApi.GetGrantsAsync(userId, cancellationToken);
        if (!grants.Any(grant => grant.RoleId == RoleIds.Director && grant.EventId == eventId))
        {
            await IdentityApi.GrantDirectorAsync(userId, eventId, cancellationToken);
        }
    }

    /// <remarks>
    /// Created directly via <see cref="EventsApiClient.CreateEventAsync"/>, not the real UI - no
    /// <see cref="Microsoft.Playwright.IPage"/> exists yet at fixture-initialization time, unlike every other
    /// Event this project creates (<see cref="E2ETestBase.CreateEventAsync"/>).
    /// </remarks>
    private async Task<Guid> EnsureRetainedEventAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<(Guid Id, string Name)> events = await Events.ListE2EEventsAsync(cancellationToken);
        foreach ((Guid id, string name) in events)
        {
            if (name == RetainedEventName)
            {
                return id;
            }
        }

        return await Events.CreateEventAsync(RetainedEventName, cancellationToken);
    }

    /// <remarks>
    /// Best-effort and logged only - see <see cref="DisposeAsync"/>'s own remarks for why, and for
    /// <see cref="VerifyRetentionAsync"/>, which runs after this and is what actually enforces the result.
    /// Deletes every <c>@example.test</c> User outside the four fixture emails, and every <c>e2e-</c> Event
    /// that isn't <see cref="RetainedEventName"/>. A properly-behaved run leaves nothing for this to find -
    /// it exists for the run that didn't (a crash, a kill, a test whose own tracked cleanup itself failed).
    /// </remarks>
    private async Task SweepOrphanedDataAsync()
    {
        try
        {
            var cancellationToken = CancellationToken.None;

            foreach ((Guid id, string name) in await Events.ListE2EEventsAsync(cancellationToken))
            {
                if (name != RetainedEventName)
                {
                    await Events.DeleteEventAsync(id, cancellationToken);
                }
            }

            string[] fixtureEmails = [AdminEmail, DirectorEmail, NoRoleEmail, InvitedEmail];
            foreach (string email in await Users.ListExampleTestEmailsAsync(cancellationToken))
            {
                if (!fixtureEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
                {
                    IdentityUserDto? user = await IdentityApi.TryGetByEmailAsync(email, cancellationToken);
                    if (user is not null)
                    {
                        await IdentityApi.DeleteUserAsync(user.Id, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(SweepLogDirectory);
            await File.AppendAllTextAsync(
                Path.Combine(SweepLogDirectory, "SWEEP_ERRORS.txt"),
                $"AspireE2EFixture's run-end sweep failed: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
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

    /// <remarks>Same recipe as <c>E2ETestBase.ResolveArtifactRoot</c> - duplicated rather than shared, since that one is private to its own type.</remarks>
    private static string ResolveArtifactRoot()
    {
        string? root = typeof(AspireE2EFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "E2EArtifactRoot")?.Value;

        return root ?? throw new InvalidOperationException(
            "E2EArtifactRoot AssemblyMetadata is missing - check VirtualLeadersGuide.E2E.Tests.csproj wasn't edited to drop it.");
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
