namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Whether the test now running records a Playwright trace and video - off for a test's first attempt, on for
/// the evidence rerun of a failed one (ADR-0076).
/// </summary>
/// <remarks>
/// A trace (screenshots, DOM snapshots, sources) and a video are only useful for a test that failed, yet
/// recording them for every test costs a lot: a serial run measured about 36% longer with tracing on, and
/// Windows Defender alone spent ~300 CPU-seconds scanning the files it writes. So a test records neither on its
/// first attempt; <see cref="RerunOnFailureTestCase"/> reruns a failed test once with
/// <see cref="RunAsRerunAsync"/>, and <see cref="E2ETestBase"/> reads <see cref="Active"/> to decide whether to
/// start tracing and video. <c>VLG_E2E_TRACE_ALL=1</c> records on every attempt instead, for a failure too
/// intermittent to reproduce on the rerun.
/// </remarks>
internal static class E2ECaptureMode
{
    /// <remarks>An <see cref="AsyncLocal{T}"/>, not a plain static: test classes run in parallel (ADR-0075), and only the rerun's own async flow may see this as true.</remarks>
    private static readonly AsyncLocal<bool> RerunScope = new();

    /// <summary>Whether <c>VLG_E2E_TRACE_ALL=1</c> is set - record every attempt, and never rerun.</summary>
    public static bool TraceAll { get; } = Environment.GetEnvironmentVariable("VLG_E2E_TRACE_ALL") == "1";

    /// <summary>Whether the test now running is the evidence rerun of a failed one.</summary>
    public static bool IsRerun => RerunScope.Value;

    /// <summary>Whether the test now running should record a trace and video.</summary>
    public static bool Active => TraceAll || RerunScope.Value;

    /// <summary>Runs <paramref name="body"/> with <see cref="IsRerun"/> true for everything it starts.</summary>
    /// <param name="body">The rerun of one test case.</param>
    public static async Task RunAsRerunAsync(Func<Task> body)
    {
        RerunScope.Value = true;
        try
        {
            await body();
        }
        finally
        {
            RerunScope.Value = false;
        }
    }
}
