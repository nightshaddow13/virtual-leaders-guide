using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Base class for every E2E test in this project. Captures <c>trace.zip</c>, <c>screenshot.png</c>,
/// <c>video.webm</c>, <c>page.html</c>, and any <see cref="AspireE2EFixture.EmailSink"/> contents under
/// <c>artifacts/e2e/&lt;run timestamp&gt;/&lt;Class&gt;.&lt;Method&gt;/</c> for a failed test, and leaves
/// nothing behind for a passing one. Also deletes every User/Event a derived test tracked via
/// <see cref="TrackUser"/>/<see cref="TrackEvent"/>/<see cref="CreateTrackedUserAsync"/>/
/// <see cref="CreateEventAsync"/>, unless <c>VLG_E2E_KEEP_DATA=1</c> is set (ADR-0039).
/// </summary>
/// <remarks>
/// Extends <see cref="PageTest"/> directly rather than wrapping it, so xUnit's constructor-injection of
/// <see cref="AspireE2EFixture"/> via <see cref="AspireE2ECollection"/> keeps working unchanged on every
/// derived test class - it's resolved purely by matching the concrete class's own constructor parameter type,
/// independently of this base class's <see cref="IAsyncLifetime"/> chain. See ADR-0028 for why this class
/// decides pass/fail itself via <see cref="RunAsync"/> rather than
/// <c>Microsoft.Playwright.Xunit</c>'s own <c>ExceptionCapturer</c>.
/// </remarks>
public abstract class E2ETestBase(AspireE2EFixture fixture) : PageTest
{
    /// <remarks>
    /// A conservative margin under Windows' 260-char <c>MAX_PATH</c>, not the literal ceiling (ADR-0028) -
    /// this machine has <c>LongPathsEnabled=0</c>, and leaving headroom means a resolved path failing this
    /// check is caught here with a clear <c>TOO_LONG.txt</c> entry, rather than surfacing later as a raw
    /// <see cref="IOException"/> from deep inside Playwright's own file-write call.
    /// </remarks>
    private const int MaxSafePathLength = 250;

    /// <remarks>
    /// Declared before <see cref="RunRoot"/> and deliberately not inlined into it - static field/property
    /// initializers run in textual declaration order, so <see cref="RunRoot"/>'s initializer needs this one
    /// to have already run.
    /// </remarks>
    private static string ArtifactRoot { get; } = E2EArtifactRoot.Resolve();

    /// <remarks>
    /// One folder per *run*, not per test: a static field initializer runs exactly once per test-assembly
    /// load, and every E2E test class shares that one process via <see cref="AspireE2ECollection"/>
    /// (ADR-0025) - so every failure in a run lands under the same timestamp. Not created here (see
    /// <see cref="ResolveArtifactPath"/>) - a run where every test passes must leave no <c>artifacts/e2e/</c>
    /// folder behind at all (AC #2).
    /// </remarks>
    private static readonly string RunRoot = Path.Combine(ArtifactRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    /// <summary>
    /// The running Aspire stack, hoisted here so every derived test class gets it without redeclaring its own
    /// constructor-injected field.
    /// </summary>
    protected AspireE2EFixture Fixture { get; } = fixture;

    /// <remarks>Defaults to <see langword="false"/> on purpose - see ADR-0028.</remarks>
    private bool _passed;
    private string _testName = "";

    /// <remarks>
    /// Deleted in <see cref="DisposeAsync"/> (ADR-0039), in the reverse order they were tracked. Populated
    /// only by <see cref="TrackEvent"/>/<see cref="TrackUser"/>/<see cref="CreateEventAsync"/>/
    /// <see cref="CreateTrackedUserAsync"/> - a test that seeds data any other way (directly against
    /// <see cref="AspireE2EFixture.IdentityApi"/>/<see cref="AspireE2EFixture.Events"/>, not through these)
    /// must track it explicitly or it leaks.
    /// </remarks>
    private readonly List<Guid> _trackedEventIds = [];
    private readonly List<string> _trackedUserIds = [];

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Screenshots</c>/<c>Snapshots</c>/<c>Sources</c> must all be true for AC #4's time-travel view,
    /// screenshot filmstrip, and network/console log to be populated when <c>trace.zip</c> is later opened -
    /// dropping any one of them leaves that part of the viewer blank.
    /// </remarks>
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// See ADR-0028 for why every failure-path capture below goes through <see cref="TryCaptureAsync"/> -
    /// a capture failure must never propagate out of this method and corrupt the test's already-correctly-
    /// reported failure. <see cref="IVideo"/> is grabbed before the context closes - Playwright only
    /// finalizes a video file once its context does, so the handle has to be captured now even though its
    /// resolved <c>Path</c> isn't available yet. <c>Tracing.StopAsync(path: null)</c> still discards tracing
    /// cleanly, so a too-long path (<see cref="ResolveArtifactPath"/>) costs only that one file, not the
    /// whole capture.
    /// </remarks>
    public override async Task DisposeAsync()
    {
        IVideo? video = Page.Video;

        string testDir = Path.Combine(RunRoot, $"{GetType().Name}.{_testName}");

        if (_passed)
        {
            await Context.Tracing.StopAsync();
        }
        else
        {
            await TryCaptureAsync(testDir, "trace.zip", async () =>
            {
                string? tracePath = ResolveArtifactPath(testDir, "trace.zip");
                await Context.Tracing.StopAsync(tracePath is null ? null : new TracingStopOptions { Path = tracePath });
            });

            await TryCaptureAsync(testDir, "screenshot.png", async () =>
            {
                string? screenshotPath = ResolveArtifactPath(testDir, "screenshot.png");
                if (screenshotPath is not null)
                {
                    await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                }
            });

            await TryCaptureAsync(testDir, "page.html", async () =>
            {
                string? pageHtmlPath = ResolveArtifactPath(testDir, "page.html");
                if (pageHtmlPath is not null)
                {
                    await File.WriteAllTextAsync(pageHtmlPath, await Page.ContentAsync());
                }
            });

            await TryCaptureAsync(testDir, "emails", () =>
            {
                string emailsDir = Path.Combine(testDir, "emails");
                foreach (string sourcePath in Fixture.EmailSink.FilePaths)
                {
                    string? destinationPath = ResolveArtifactPath(emailsDir, Path.GetFileName(sourcePath));
                    if (destinationPath is not null)
                    {
                        File.Copy(sourcePath, destinationPath, overwrite: true);
                    }
                }

                return Task.CompletedTask;
            });
        }

        if (!AspireE2EFixture.KeepData)
        {
            await TryCaptureAsync(testDir, "cleanup.txt", () => CleanupTrackedDataAsync(testDir));
        }

        await base.DisposeAsync();

        if (video is not null)
        {
            string videoPath = await video.PathAsync();

            if (_passed)
            {
                await TryCaptureAsync(testDir, "video.webm", () => DeleteWithRetryAsync(videoPath));
            }
            else
            {
                await TryCaptureAsync(testDir, "video.webm", async () =>
                {
                    string? destinationPath = ResolveArtifactPath(testDir, "video.webm");
                    if (destinationPath is not null)
                    {
                        await MoveWithRetryAsync(videoPath, destinationPath);
                    }
                    else
                    {
                        await DeleteWithRetryAsync(videoPath);
                    }
                });
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Records video for every test, not just failing ones - Playwright only finalizes a video once its
    /// context closes, so "only record on failure" isn't an option. <see cref="DisposeAsync"/> deletes the
    /// file instead when the test passed (AC #2).
    /// </remarks>
    /// <remarks>
    /// <c>TimezoneId = "UTC"</c> pins the browser's <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>
    /// (P2-15, #102) - without it Playwright defaults to the host machine's own zone, and a rendered Event
    /// date range (<c>EventDateRange</c> converts to each viewer's browser timezone, ADR-0043) would vary
    /// with whatever CI agent or developer machine ran the suite. <c>RecordVideoDir</c> is unrelated - see
    /// this method's other remarks.
    /// </remarks>
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        RecordVideoDir = Path.GetTempPath(),
        TimezoneId = "UTC"
    };

    /// <summary>
    /// Wraps a test body so this class - not <c>Microsoft.Playwright.Xunit</c>'s own <c>ExceptionCapturer</c>
    /// (see this type's own header comment) - decides pass/fail. Call this as the entire body of every
    /// <c>[Fact]</c>/<c>[Theory]</c> in this project.
    /// </summary>
    /// <param name="testBody">The test's own navigation and assertions.</param>
    /// <param name="testName">
    /// Supplied automatically via <see cref="CallerMemberNameAttribute"/> for a <c>[Fact]</c>. A
    /// <c>[Theory]</c> should pass its own row-inclusive name explicitly (e.g.
    /// <c>$"{nameof(TheMethod)}_{data}"</c>) - <c>[CallerMemberName]</c> alone can't distinguish rows of the
    /// same method, and this project pins xUnit v2, where <c>TestContext.Current</c> (which would otherwise
    /// recover the running test's identity) doesn't exist.
    /// </param>
    /// <exception cref="Exception">
    /// Whatever <paramref name="testBody"/> itself throws - propagated unchanged so xUnit still reports the
    /// test's real failure. This method adds no <c>try</c>/<c>catch</c> of its own.
    /// </exception>
    protected async Task RunAsync(Func<Task> testBody, [CallerMemberName] string testName = "")
    {
        _testName = testName;
        await testBody();
        _passed = true;
    }

    /// <remarks>
    /// <see cref="AspireE2EFixture.AdminAllowlistedEmail"/> is one value shared by every test in this run
    /// (ADR-0025 - one Aspire stack, one fixture instance), unlike every other account most tests create,
    /// which is freshly guid-suffixed per call - so this checks for an existing account before creating
    /// one, rather than assuming a fresh account the way most other seeding calls in this project do.
    /// </remarks>
    protected async Task SignInAsAdminAsync()
    {
        if (!await Fixture.IdentityApi.ExistsAsync(Fixture.AdminAllowlistedEmail, CancellationToken.None))
        {
            await Fixture.IdentityApi.CreateUserAsync(
                Fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword, CancellationToken.None);
        }

        await new LoginPage(Page).SignInAsync(
            Fixture.WebBaseUrl, Fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword);
    }

    /// <summary>Registers <paramref name="id"/> for deletion in <see cref="DisposeAsync"/> (ADR-0039).</summary>
    /// <remarks>
    /// For a User seeded some way other than <see cref="CreateTrackedUserAsync"/> (e.g. one whose
    /// <see cref="IdentityUserDto"/> a test needs to keep hold of for <c>LockOutAsync</c>) - most tests want
    /// <see cref="CreateTrackedUserAsync"/> instead, which tracks for you.
    /// </remarks>
    protected void TrackUser(string id) => _trackedUserIds.Add(id);

    /// <summary>Registers <paramref name="id"/> for deletion in <see cref="DisposeAsync"/> (ADR-0039).</summary>
    protected void TrackEvent(Guid id) => _trackedEventIds.Add(id);

    /// <summary>
    /// Looks <paramref name="email"/> up and tracks its User for automatic cleanup (ADR-0039) - for a User
    /// created through the real invite UI (<c>DirectorInviteScenarios</c>) rather than seeded via
    /// <see cref="CreateTrackedUserAsync"/>, so no id was ever returned to track directly.
    /// </summary>
    /// <param name="email">The email to look up - normalized the same way <see cref="IdentityApiClient.CreateUserAsync"/> does.</param>
    protected async Task TrackUserByEmailAsync(string email)
    {
        IdentityUserDto? user = await Fixture.IdentityApi.TryGetByEmailAsync(email, CancellationToken.None);
        if (user is not null)
        {
            TrackUser(user.Id);
        }
    }

    /// <summary>
    /// Seeds a fresh, guid-suffixed User via <see cref="AspireE2EFixture.IdentityApi"/> and tracks it for
    /// automatic cleanup (ADR-0039) - the throwaway-account equivalent of this class's fixture-account
    /// helpers.
    /// </summary>
    /// <param name="emailPrefix">
    /// A short label identifying the scenario (e.g. <c>"e2e-locked-out"</c>) - guid-suffixed to
    /// <c>{emailPrefix}-{guid}@example.test</c>, matching every call site's own prior convention.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying seed call.</param>
    /// <returns>The created User, as <c>Api</c> stored it.</returns>
    protected async Task<IdentityUserDto> CreateTrackedUserAsync(string emailPrefix, CancellationToken cancellationToken)
    {
        string email = $"{emailPrefix}-{Guid.NewGuid():n}@example.test";
        IdentityUserDto user =
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, cancellationToken);
        TrackUser(user.Id);
        return user;
    }

    /// <summary>
    /// Creates an Event via the real UI and tracks it for automatic cleanup (ADR-0039). Shared by every
    /// scenario class that creates Events, rather than each keeping its own copy - previously duplicated
    /// verbatim between <c>EventManagementScenarios</c> and <c>DirectorInviteScenarios</c>.
    /// </summary>
    /// <param name="label">
    /// A short display label (e.g. <c>"Summer Camporee"</c>) - the actual <c>Name</c> submitted is
    /// <c>"e2e-{label} {guid}"</c>: guid-suffixed for uniqueness against <c>Event.Name</c>'s unique index, and
    /// <c>e2e-</c>-prefixed so <see cref="EventsApiClient.ListE2EEventsAsync"/>'s sweep filter can tell this
    /// Event apart from anything a developer made by hand (ADR-0039) - without the prefix, that filter would
    /// never match anything this suite creates.
    /// </param>
    /// <returns>The created Event's id, and its full (already <c>e2e-</c>-prefixed and guid-suffixed) Name.</returns>
    /// <remarks>
    /// The URL-change assertion below carries a longer-than-default timeout - <c>/dashboard/events/new</c>
    /// is an <c>InteractiveServer</c> page (ADR-0034), so a click has to survive a real SignalR round trip,
    /// not just a DOM update; the first such round trip in a fresh browser context (cold circuit connect)
    /// can comfortably exceed Playwright's 5s assertion default on a loaded machine.
    /// </remarks>
    protected async Task<(Guid Id, string Name)> CreateEventAsync(string label)
    {
        string name = $"e2e-{label} {Guid.NewGuid():n}";

        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard/events/new").ToString());
        await Page.Locator("#Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create event" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Regex(@"dashboard/events/[0-9a-f-]{36}$"), new PageAssertionsToHaveURLOptions { Timeout = 20_000 });

        string path = new Uri(Page.Url).AbsolutePath;
        Guid id = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
        TrackEvent(id);
        return (id, name);
    }

    /// <summary>The URL of an Event's editor page, given its id.</summary>
    /// <param name="eventId">The Event to link to.</param>
    protected string EventEditorUrl(Guid eventId) => new Uri(Fixture.WebBaseUrl, $"dashboard/events/{eventId}").ToString();

    /// <summary>Signs the current session out via the site header's real sign-out form.</summary>
    /// <remarks>
    /// The only sign-out affordance is the header's real <c>POST Account/Logout</c> form
    /// (<c>SignOutForm.razor</c>) - clicking it, rather than clearing cookies directly, exercises the same
    /// path a real session change goes through and keeps this class's tests from assuming cookie storage
    /// details.
    /// </remarks>
    protected async Task SignOutAsync()
    {
        await Page.GotoAsync(Fixture.WebBaseUrl.ToString());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
    }

    /// <remarks>
    /// Deletes Events before Users, each in the reverse order tracked - no FK requires this specific order
    /// (deleting either side cascades that Event/User's own <c>UserRole</c> rows independently), it's just a
    /// consistent LIFO teardown. On a failed test, also records what was deleted to <c>cleanup.txt</c>
    /// alongside this test's other artifacts, so the trace has a record of what cleanup removed. Wrapped by
    /// <see cref="TryCaptureAsync"/> at the call site - a cleanup failure must never turn a passing test red
    /// (ADR-0028).
    /// </remarks>
    private async Task CleanupTrackedDataAsync(string testDir)
    {
        var deleted = new List<string>();

        foreach (Guid eventId in Enumerable.Reverse(_trackedEventIds))
        {
            await Fixture.Events.DeleteEventAsync(eventId, CancellationToken.None);
            deleted.Add($"event {eventId}");
        }

        foreach (string userId in Enumerable.Reverse(_trackedUserIds))
        {
            await Fixture.IdentityApi.DeleteUserAsync(userId, CancellationToken.None);
            deleted.Add($"user {userId}");
        }

        if (!_passed && deleted.Count > 0)
        {
            Directory.CreateDirectory(testDir);
            await File.WriteAllLinesAsync(Path.Combine(testDir, "cleanup.txt"), deleted);
        }
    }

    /// <remarks>
    /// Swallows any exception into a <c>CAPTURE_ERRORS.txt</c> entry at the run root instead of letting it
    /// escape <see cref="DisposeAsync"/> - see ADR-0028 for why. Deliberately broader than
    /// <see cref="ResolveArtifactPath"/>'s own <c>TOO_LONG.txt</c> (a known, named failure mode); this is
    /// the general backstop for anything else that goes wrong capturing a specific file - a locked handle,
    /// a transient I/O error - without needing to have been anticipated by name.
    /// </remarks>
    private static async Task TryCaptureAsync(string testDir, string fileName, Func<Task> capture)
    {
        try
        {
            await capture();
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(RunRoot);
            await File.AppendAllTextAsync(
                Path.Combine(RunRoot, "CAPTURE_ERRORS.txt"),
                $"{fileName} for {Path.GetFileName(testDir)} failed: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
    }

    /// <remarks>
    /// Playwright's video encoder can hold the temp file's handle open for a short window after
    /// <c>base.DisposeAsync()</c> returns and <c>IVideo.PathAsync()</c> resolves (ADR-0028) - retried
    /// briefly (up to ~3s total) rather than treated as a hard failure on the first try, since the handle
    /// reliably releases well within that window in practice; <see cref="TryCaptureAsync"/> is still the
    /// backstop if it doesn't.
    /// </remarks>
    private static async Task MoveWithRetryAsync(string sourcePath, string destinationPath)
    {
        const int maxAttempts = 5;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    /// <remarks>Same handle-release race as <see cref="MoveWithRetryAsync"/>, for the passing-test cleanup path.</remarks>
    private static async Task DeleteWithRetryAsync(string path)
    {
        const int maxAttempts = 5;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    /// <remarks>
    /// Resolves the full path for one artifact file, creating its test directory if the path fits, or
    /// recording a <c>TOO_LONG.txt</c> entry at the run root and returning <see langword="null"/> if it
    /// doesn't. Never throws and never masks the test's real failure (ADR-0028) - a path-length problem only
    /// ever costs evidence, not correctness of the reported result. The marker lives at
    /// <see cref="RunRoot"/>, one level up, since a marker at <paramref name="testDir"/>'s own level doesn't
    /// help when <paramref name="testDir"/> itself is the overflow.
    /// </remarks>
    private static string? ResolveArtifactPath(string testDir, string fileName)
    {
        string path = Path.Combine(testDir, fileName);

        if (path.Length < MaxSafePathLength)
        {
            Directory.CreateDirectory(testDir);
            return path;
        }

        Directory.CreateDirectory(RunRoot);
        File.AppendAllText(
            Path.Combine(RunRoot, "TOO_LONG.txt"),
            $"{fileName} for {Path.GetFileName(testDir)} would exceed the path-length limit: {path}{Environment.NewLine}");
        return null;
    }
}
