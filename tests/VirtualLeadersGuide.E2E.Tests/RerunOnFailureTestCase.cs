using Xunit.Abstractions;
using Xunit.Sdk;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Wraps an xUnit test case so a failure is rerun once, with a trace and video recorded, purely to gather
/// evidence - the test is still reported as failed (ADR-0076).
/// </summary>
/// <remarks>
/// The first attempt's messages are held back until the rerun finishes, then forwarded unchanged, so the
/// reported result is always the first attempt's: a rerun that passes means the failure did not reproduce
/// (<see cref="E2ETestBase"/> notes that next to the rerun's artifacts), never that the test passed. The rerun's
/// own messages are discarded, and nothing it throws can change the reported result. xUnit v2 has no retry
/// feature, so this decorates each case where <see cref="AspireE2ETestFrameworkExecutor"/> hands them to the
/// assembly runner - which also keeps the test classes free of any retry attribute.
/// </remarks>
internal sealed class RerunOnFailureTestCase : LongLivedMarshalByRefObject, IXunitTestCase
{
    private IXunitTestCase _inner = null!;

    /// <summary>Required by xUnit's serializer; not for application code.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes", error: true)]
    public RerunOnFailureTestCase()
    {
    }

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    /// <param name="inner">The test case to run and, on failure, rerun.</param>
    public RerunOnFailureTestCase(IXunitTestCase inner) => _inner = inner;

    /// <inheritdoc/>
    public string DisplayName => _inner.DisplayName;

    /// <inheritdoc/>
    public string SkipReason => _inner.SkipReason;

    /// <inheritdoc/>
    public ISourceInformation SourceInformation
    {
        get => _inner.SourceInformation;
        set => _inner.SourceInformation = value;
    }

    /// <inheritdoc/>
    public ITestMethod TestMethod => _inner.TestMethod;

    /// <inheritdoc/>
    public object[] TestMethodArguments => _inner.TestMethodArguments;

    /// <inheritdoc/>
    public Dictionary<string, List<string>> Traits => _inner.Traits;

    /// <inheritdoc/>
    public string UniqueID => _inner.UniqueID;

    /// <inheritdoc/>
    public IMethodInfo Method => _inner.Method;

    /// <inheritdoc/>
    public Exception InitializationException => _inner.InitializationException;

    /// <inheritdoc/>
    public int Timeout => _inner.Timeout;

    /// <inheritdoc/>
    public void Serialize(IXunitSerializationInfo info) => info.AddValue("inner", _inner);

    /// <inheritdoc/>
    public void Deserialize(IXunitSerializationInfo info) => _inner = info.GetValue<IXunitTestCase>("inner");

    /// <inheritdoc/>
    public async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var firstAttemptMessages = new HeldMessageBus(messageBus);

        RunSummary summary = await _inner.RunAsync(
            diagnosticMessageSink, firstAttemptMessages, constructorArguments, aggregator, cancellationTokenSource);

        if (summary.Failed > 0 && !E2ECaptureMode.TraceAll && !cancellationTokenSource.IsCancellationRequested)
        {
            diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                $"E2E: '{DisplayName}' failed; rerunning once with a trace and video for evidence. " +
                "The reported result stays the first attempt's."));

            try
            {
                await E2ECaptureMode.RunAsRerunAsync(() => _inner.RunAsync(
                    diagnosticMessageSink,
                    new DiscardingMessageBus(),
                    constructorArguments,
                    new ExceptionAggregator(),
                    cancellationTokenSource));
            }
            catch (Exception ex)
            {
                diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                    $"E2E: the evidence rerun of '{DisplayName}' threw {ex.GetType().Name}: {ex.Message}"));
            }
        }

        firstAttemptMessages.Release();
        return summary;
    }

    /// <summary>Holds every message until <see cref="Release"/>, then forwards them in order to the real bus.</summary>
    private sealed class HeldMessageBus(IMessageBus target) : IMessageBus
    {
        private readonly List<IMessageSinkMessage> _held = [];

        /// <inheritdoc/>
        public bool QueueMessage(IMessageSinkMessage message)
        {
            lock (_held)
            {
                _held.Add(message);
            }

            return true;
        }

        /// <summary>Forwards everything held so far to the real bus.</summary>
        public void Release()
        {
            lock (_held)
            {
                foreach (IMessageSinkMessage message in _held)
                {
                    target.QueueMessage(message);
                }

                _held.Clear();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }

    /// <summary>Accepts and drops every message - for the rerun, whose result must never be reported.</summary>
    private sealed class DiscardingMessageBus : IMessageBus
    {
        /// <inheritdoc/>
        public bool QueueMessage(IMessageSinkMessage message) => true;

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
