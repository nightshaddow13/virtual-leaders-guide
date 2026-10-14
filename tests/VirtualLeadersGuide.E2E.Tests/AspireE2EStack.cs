namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Holds the one <see cref="AspireE2EFixture"/> every E2E test class in the run shares (ADR-0075).
/// <see cref="AspireE2ETestAssemblyRunner"/> is its only writer: it calls <see cref="BootAsync"/> before the
/// first test and <see cref="ShutdownAsync"/> after the last.
/// </summary>
internal static class AspireE2EStack
{
    /// <remarks>
    /// Assigned <em>before</em> <see cref="AspireE2EFixture.InitializeAsync"/> runs, not after it succeeds, so
    /// <see cref="ShutdownAsync"/> can still dispose containers and processes left behind by a boot that
    /// failed part-way through.
    /// </remarks>
    private static AspireE2EFixture? s_fixture;

    private static Exception? s_bootFailure;

    /// <summary>The running stack.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack failed to boot (the message is the fixture's own, with Docker/dev-certs/AppHost guidance), or
    /// was never booted because the assembly runner did not run.
    /// </exception>
    public static AspireE2EFixture Current
    {
        get
        {
            if (s_bootFailure is not null)
            {
                throw new InvalidOperationException(s_bootFailure.Message, s_bootFailure);
            }

            return s_fixture ?? throw new InvalidOperationException(
                "AspireE2EStack was never booted - AspireE2ETestFramework must be the assembly's TestFramework.");
        }
    }

    /// <summary>
    /// Boots the stack. Never throws: a failure is stored and logged to <c>BOOT_ERROR.txt</c>, then rethrown
    /// from <see cref="Current"/> in every test.
    /// </summary>
    /// <remarks>
    /// xUnit v2 does not run <c>AfterTestAssemblyStartingAsync</c> through the runner's <c>Aggregator</c>, so
    /// a throw out of it would abort the whole run with one opaque error and no per-test results. Capturing
    /// instead reproduces what a failing collection fixture used to do: every test fails, each with the
    /// fixture's own message.
    /// </remarks>
    public static async Task BootAsync()
    {
        s_fixture = new AspireE2EFixture();

        try
        {
            await s_fixture.InitializeAsync();
        }
        catch (Exception ex)
        {
            s_bootFailure = ex;
            E2EArtifactLog.Append(
                Path.Combine(E2EArtifactRoot.Resolve(), "BOOT_ERROR.txt"),
                $"AspireE2EStack failed to boot: {ex}");
        }
    }

    /// <summary>
    /// Sweeps, verifies retention (ADR-0039) and disposes the stack, or only disposes it if it never finished
    /// booting. Lets a retention violation throw, for the caller to hand to the runner's <c>Aggregator</c>.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (s_fixture is not null)
        {
            await s_fixture.DisposeAsync();
        }
    }
}
