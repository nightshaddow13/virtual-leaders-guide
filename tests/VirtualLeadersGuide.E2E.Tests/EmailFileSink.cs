using System.Net;
using System.Text.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Reads the JSON files Web's <c>FileSinkEmailSender</c> writes into <see cref="Directory"/> - the test-side
/// half of the config-selected email sender (P2.1-4, #62; ADR-0032).
/// </summary>
/// <remarks>
/// One instance per <see cref="AspireE2EFixture"/>, sharing its lifetime - see
/// <see cref="AspireE2EFixture.EmailSink"/>.
/// </remarks>
public sealed class EmailFileSink : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The directory Web writes to, passed to it as <c>Email:FileSinkDirectory</c>.</summary>
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"vlg-e2e-email-{Guid.NewGuid():n}");

    public EmailFileSink() => System.IO.Directory.CreateDirectory(Directory);

    /// <summary>
    /// Files currently in <see cref="Directory"/> with a <c>.json</c> extension, for
    /// <see cref="E2ETestBase"/> to copy into a failed test's artifact folder.
    /// </summary>
    public IReadOnlyList<string> FilePaths =>
        System.IO.Directory.Exists(Directory) ? [.. EnumerateEmailFiles()] : [];

    /// <summary>
    /// Polls <see cref="Directory"/> until an email addressed to <paramref name="toEmail"/> appears, and
    /// returns it.
    /// </summary>
    /// <param name="toEmail">The recipient to match, case-insensitively, against <see cref="SentEmailDto.To"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait early.</param>
    /// <exception cref="TimeoutException">
    /// No matching email appeared within the wait budget. The message lists every file actually present, the
    /// same "make the failure actionable" approach <see cref="AspireE2EFixture"/> takes for its own probes.
    /// </exception>
    public async Task<SentEmailDto> WaitForEmailAsync(string toEmail, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(WaitTimeout);

        while (true)
        {
            SentEmailDto? match = TryFindEmailFor(toEmail);
            if (match is not null)
            {
                return match;
            }

            try
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                IEnumerable<string> present = EnumerateEmailFiles().Select(Path.GetFileName)!;
                throw new TimeoutException(
                    $"No email for '{toEmail}' appeared under {Directory} within {WaitTimeout}. " +
                    $"Files present: [{string.Join(", ", present)}].");
            }
        }
    }

    /// <summary>
    /// Whether an email addressed to <paramref name="toEmail"/> is currently present in
    /// <see cref="Directory"/> - a point-in-time check, not a wait. See <see cref="WaitForEmailAsync"/> for
    /// why this alone can't prove a negative; callers need a happens-before first.
    /// </summary>
    public bool HasEmailFor(string toEmail) => TryFindEmailFor(toEmail) is not null;

    /// <remarks>Best-effort - a locked file here would only be evidence, never the cause of a real test failure.</remarks>
    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private SentEmailDto? TryFindEmailFor(string toEmail)
    {
        foreach (string path in EnumerateEmailFiles())
        {
            SentEmailDto? email = TryReadEmail(path);
            if (email is not null && string.Equals(email.To, toEmail, StringComparison.OrdinalIgnoreCase))
            {
                return email;
            }
        }

        return null;
    }

    /// <remarks>
    /// <c>*.json</c> globbing on Windows also matches longer extensions (e.g. <c>*.json.tmp</c>), so
    /// <see cref="Path.GetExtension(string)"/> re-checks the match exactly - same defense
    /// <c>FileSinkEmailSender</c>'s own temp-file naming relies on from the writing side.
    /// </remarks>
    private IEnumerable<string> EnumerateEmailFiles() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            : [];

    /// <remarks>
    /// A file can still be mid-<c>File.Move</c> when observed, or briefly locked - <see cref="IOException"/>
    /// and <see cref="JsonException"/> both mean "not ready yet, try again next poll," not "this email doesn't
    /// exist." <see cref="SentEmailDto.Payload"/> is HTML-decoded here: <c>ForgotPassword.razor</c> HTML-
    /// encodes the callback URL before ever handing it to the sender, so <c>FileSinkEmailSender</c> writes it
    /// still encoded, and a caller navigating straight to it would otherwise get a mangled URL.
    /// </remarks>
    private static SentEmailDto? TryReadEmail(string path)
    {
        try
        {
            SentEmailDto? email = JsonSerializer.Deserialize<SentEmailDto>(File.ReadAllText(path), SerializerOptions);
            return email is null ? null : email with { Payload = WebUtility.HtmlDecode(email.Payload) };
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
