using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VirtualLeadersGuide.AppHost;

/// <summary>
/// Picks the port offset that lets the app run from several git worktrees at once.
/// </summary>
/// <remarks>
/// Only ports need this: Aspire already names the SQL Server and Azurite data volumes from a hash of the
/// AppHost path, so each checkout has its own data.
/// </remarks>
public static partial class WorktreePorts
{
    /// <summary>The environment variable that overrides the computed offset.</summary>
    public const string OverrideVariable = "VLG_PORT_OFFSET";

    /// <summary>Returns the offset to add to every fixed local port.</summary>
    /// <param name="startDirectory">A directory inside the checkout.</param>
    /// <param name="overrideValue">The value of <see cref="OverrideVariable"/>, if set.</param>
    /// <returns>
    /// <paramref name="overrideValue"/> when it is a non-negative integer. Otherwise 0 for the main checkout,
    /// or a multiple of 10 from 10 to 900 that stays the same for a given linked worktree path.
    /// </returns>
    /// <remarks>
    /// Git writes <c>.git</c> as a file in a linked worktree and as a directory in the main checkout. Two
    /// worktrees can land on the same offset; <see cref="OverrideVariable"/> is the way out.
    /// </remarks>
    public static int OffsetFor(string startDirectory, string? overrideValue)
    {
        if (int.TryParse(overrideValue, out var offset) && offset >= 0)
        {
            return offset;
        }

        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(gitPath))
            {
                return 0;
            }

            if (File.Exists(gitPath))
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(directory.FullName.ToLowerInvariant()));

                return (int)(BitConverter.ToUInt32(hash) % 90 + 1) * 10;
            }
        }

        return 0;
    }

    /// <summary>Adds <paramref name="offset"/> to every port in a <c>;</c>-separated list of URLs.</summary>
    /// <param name="urls">The URL list, such as the value of <c>ASPNETCORE_URLS</c>.</param>
    /// <param name="offset">The offset to add.</param>
    /// <returns>The URL list with shifted ports. URLs without an explicit port are unchanged.</returns>
    public static string Shift(string urls, int offset) =>
        PortPattern().Replace(urls, match => $":{int.Parse(match.Groups[1].Value) + offset}");

    [GeneratedRegex(@":(\d+)(?=[/;]|$)")]
    private static partial Regex PortPattern();
}
