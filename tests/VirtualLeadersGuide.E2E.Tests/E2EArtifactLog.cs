namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Appends one line to a shared artifact log (<c>CAPTURE_ERRORS.txt</c>, <c>TOO_LONG.txt</c>,
/// <c>SWEEP_ERRORS.txt</c>, <c>BOOT_ERROR.txt</c>) - the single place every such append goes through.
/// </summary>
/// <remarks>
/// Test classes run in parallel (ADR-0075), so two tests can append to the same log at the same moment, and an
/// unsynchronized <see cref="File.AppendAllText(string, string?)"/> would throw an <see cref="IOException"/>
/// for the loser. That would escape <see cref="E2ETestBase.DisposeAsync"/> and break ADR-0028's rule that no
/// artifact-capture failure may propagate out of it. One process-wide lock is enough: every write is a single
/// short line, and this runs only on failure paths. Synchronous on purpose, so the synchronous
/// <c>ResolveArtifactPath</c> can use it too.
/// </remarks>
internal static class E2EArtifactLog
{
    private static readonly object Gate = new();

    /// <summary>Appends <paramref name="line"/> and a newline to <paramref name="path"/>, creating its directory first.</summary>
    /// <param name="path">The log file's full path.</param>
    /// <param name="line">The text to append, without a trailing newline.</param>
    public static void Append(string path, string line)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
