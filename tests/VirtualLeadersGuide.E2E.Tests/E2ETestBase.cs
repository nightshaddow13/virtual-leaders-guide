using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace VirtualLeadersGuide.E2E.Tests;

// Microsoft.Playwright.Xunit's own pass/fail signal - its ExceptionCapturer - hooks
// AppDomain.FirstChanceException, so any caught-and-handled exception anywhere in the process (not just an
// unhandled one escaping this test's own body) marks a passing test as failed. This project decides pass/fail
// itself instead: RunAsync only ever flips _passed to true once a wrapped test body returns without throwing,
// and nothing here subscribes to FirstChanceException at all. See ADR-0028.
/// <summary>
/// Base class for every E2E test in this project. Captures <c>trace.zip</c>, <c>screenshot.png</c>,
/// <c>video.webm</c>, and <c>page.html</c> under <c>artifacts/e2e/&lt;run timestamp&gt;/&lt;Class&gt;.&lt;Method&gt;/</c>
/// for a failed test, and leaves nothing behind for a passing one.
/// </summary>
/// <remarks>
/// Extends <see cref="PageTest"/> directly rather than wrapping it, so xUnit's constructor-injection of
/// <see cref="AspireE2EFixture"/> via <see cref="AspireE2ECollection"/> keeps working unchanged on every
/// derived test class - it's resolved purely by matching the concrete class's own constructor parameter type,
/// independently of this base class's <see cref="IAsyncLifetime"/> chain.
/// </remarks>
public abstract class E2ETestBase(AspireE2EFixture fixture) : PageTest
{
    // A conservative margin under Windows' 260-char MAX_PATH, not the literal ceiling - this machine has
    // LongPathsEnabled=0, and leaving headroom means a resolved path failing this check is caught here with a
    // clear TOO_LONG.txt entry, rather than surfacing later as a raw IOException from deep inside Playwright's
    // own file-write call.
    private const int MaxSafePathLength = 250;

    // Declared before RunRoot below and deliberately not inlined into it - static field/property initializers
    // run in textual declaration order, so RunRoot's initializer needs this one to have already run.
    private static string ArtifactRoot { get; } = ResolveArtifactRoot();

    // One folder per *run*, not per test: a static field initializer runs exactly once per test-assembly
    // load, and every E2E test class shares that one process via AspireE2ECollection (ADR-0025) - so every
    // failure in a run lands under the same timestamp. Not created here (see ResolveArtifactPath) - a run
    // where every test passes must leave no artifacts/e2e/ folder behind at all (AC #2).
    private static readonly string RunRoot = Path.Combine(ArtifactRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    /// <summary>
    /// The running Aspire stack, hoisted here so every derived test class gets it without redeclaring its own
    /// constructor-injected field.
    /// </summary>
    protected AspireE2EFixture Fixture { get; } = fixture;

    // Defaults to false on purpose: a test that forgets the RunAsync wrapper over-collects artifacts (every
    // capture path below runs, since _passed never flips true) rather than silently losing the evidence it
    // was written to preserve.
    private bool _passed;
    private string _testName = "";

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // Screenshots/Snapshots/Sources must all be true for AC #4's time-travel view, screenshot filmstrip,
        // and network/console log to be populated when trace.zip is later opened - dropping any one of them
        // leaves that part of the viewer blank.
        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        // Grabbed before the context closes below - Playwright only finalizes a video file once its context
        // does, so the handle has to be captured now even though its resolved Path isn't available yet.
        IVideo? video = Page.Video;

        string testDir = Path.Combine(RunRoot, $"{GetType().Name}.{_testName}");

        if (_passed)
        {
            await Context.Tracing.StopAsync();
        }
        else
        {
            // Every capture below goes through TryCaptureAsync: a failure here must never propagate out of
            // DisposeAsync and corrupt the test's already-correctly-reported failure (observed directly
            // during implementation - an unguarded video move exception turned a clean single-assertion
            // failure into a confusing two-exception AggregateException). Losing one piece of evidence is
            // always an acceptable outcome; losing the real failure message never is.
            await TryCaptureAsync(testDir, "trace.zip", async () =>
            {
                // Tracing must be stopped exactly once either way - StopAsync(path: null) still discards it
                // cleanly, so a too-long path costs only this one file, not the whole capture.
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
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        // Recorded for every test, not just failing ones - Playwright only finalizes a video once its context
        // closes, so "only record on failure" isn't an option. DisposeAsync deletes the file instead when the
        // test passed (AC #2).
        RecordVideoDir = Path.GetTempPath(),
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

    // Runs one artifact capture, swallowing any exception into a CAPTURE_ERRORS.txt entry at the run root
    // instead of letting it escape DisposeAsync - see this method's call sites for why. Deliberately broader
    // than ResolveArtifactPath's own TOO_LONG.txt (a known, named failure mode); this is the general backstop
    // for anything else that goes wrong capturing a specific file - a locked handle, a transient I/O error -
    // without needing to have been anticipated by name.
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

    // Playwright's video encoder can hold the temp file's handle open for a short window after
    // base.DisposeAsync() returns and IVideo.PathAsync() resolves - observed directly during implementation as
    // a transient "file in use by another process" IOException on the very first File.Move attempt. Retried
    // briefly (up to ~3s total) rather than treated as a hard failure on the first try, since the handle
    // reliably releases well within that window in practice; TryCaptureAsync is still the backstop if it
    // doesn't.
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

    // Same handle-release race as MoveWithRetryAsync, for the passing-test cleanup path.
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

    // Resolves the full path for one artifact file, creating its test directory if the path fits, or
    // recording a TOO_LONG.txt entry at the run root and returning null if it doesn't. Never throws and never
    // masks the test's real failure - a path-length problem only ever costs evidence, not correctness of the
    // reported result. See ADR-0028.
    private static string? ResolveArtifactPath(string testDir, string fileName)
    {
        string path = Path.Combine(testDir, fileName);

        if (path.Length < MaxSafePathLength)
        {
            Directory.CreateDirectory(testDir);
            return path;
        }

        // A marker at testDir's own level doesn't help when testDir itself is the overflow - it lives at
        // RunRoot instead, one level up, which is always short enough to exist.
        Directory.CreateDirectory(RunRoot);
        File.AppendAllText(
            Path.Combine(RunRoot, "TOO_LONG.txt"),
            $"{fileName} for {Path.GetFileName(testDir)} would exceed the path-length limit: {path}{Environment.NewLine}");
        return null;
    }

    private static string ResolveArtifactRoot()
    {
        string? root = typeof(E2ETestBase).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "E2EArtifactRoot")?.Value;

        return root ?? throw new InvalidOperationException(
            "E2EArtifactRoot AssemblyMetadata is missing - check VirtualLeadersGuide.E2E.Tests.csproj wasn't edited to drop it.");
    }
}
