using System.Reflection;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Resolves the <c>E2EArtifactRoot</c> <c>AssemblyMetadata</c> value the csproj bakes in at build time
/// (<c>artifacts/e2e</c>) - shared by <see cref="E2ETestBase"/> (per-test failure artifacts, under a
/// timestamped run folder) and <see cref="AspireE2EFixture"/> (its own run-end sweep's error log, at this
/// root directly - see that type's own remarks for why it doesn't share <see cref="E2ETestBase"/>'s
/// timestamped <c>RunRoot</c>).
/// </summary>
internal static class E2EArtifactRoot
{
    /// <returns>The absolute path to <c>artifacts/e2e</c>, resolved once per call from assembly metadata.</returns>
    /// <exception cref="InvalidOperationException">The <c>E2EArtifactRoot</c> AssemblyMetadata is missing.</exception>
    public static string Resolve()
    {
        string? root = typeof(E2EArtifactRoot).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "E2EArtifactRoot")?.Value;

        return root ?? throw new InvalidOperationException(
            "E2EArtifactRoot AssemblyMetadata is missing - check VirtualLeadersGuide.E2E.Tests.csproj wasn't edited to drop it.");
    }
}
