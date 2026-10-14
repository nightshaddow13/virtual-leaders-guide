using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("VirtualLeadersGuide.E2E.Tests.AspireE2ETestFramework", "VirtualLeadersGuide.E2E.Tests")]

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// The assembly's xUnit test framework: stock xUnit v2, except that the one Aspire stack is booted before the
/// first test and swept, verified and disposed after the last, by <see cref="AspireE2ETestAssemblyRunner"/>
/// (ADR-0075). That is what lets every test class be its own collection and run in parallel without a second
/// stack.
/// </summary>
/// <remarks>
/// Needs <c>xunit.execution</c>'s <see cref="XunitTestFramework"/>, which the <c>xunit</c> package brings in
/// transitively - this is the v2 extensibility seam, and why the hold-back on xUnit v3 (ADR-0060) matters here.
/// </remarks>
/// <param name="messageSink">The framework's diagnostic message sink, supplied by xUnit.</param>
public class AspireE2ETestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    /// <inheritdoc/>
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new AspireE2ETestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

/// <summary>Runs the assembly's test cases through <see cref="AspireE2ETestAssemblyRunner"/> instead of xUnit's stock runner.</summary>
/// <param name="assemblyName">The test assembly's name.</param>
/// <param name="sourceInformationProvider">Source-location lookup, supplied by xUnit.</param>
/// <param name="diagnosticMessageSink">Diagnostic message sink, supplied by xUnit.</param>
public class AspireE2ETestFrameworkExecutor(
    AssemblyName assemblyName,
    ISourceInformationProvider sourceInformationProvider,
    IMessageSink diagnosticMessageSink)
    : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
    /// <inheritdoc/>
    protected override async void RunTestCases(
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
    {
        using var assemblyRunner = new AspireE2ETestAssemblyRunner(
            TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
        await assemblyRunner.RunAsync();
    }
}

/// <summary>
/// Boots <see cref="AspireE2EStack"/> once before any test and shuts it down once after every test, however
/// many classes run in parallel in between.
/// </summary>
/// <remarks>
/// Two facts about xUnit v2's <c>TestAssemblyRunner</c> shape this. <c>AfterTestAssemblyStartingAsync</c> is
/// not run through the <c>Aggregator</c>, so <see cref="AspireE2EStack.BootAsync"/> catches its own failures
/// rather than throw. <c>BeforeTestAssemblyFinishedAsync</c> runs after the <c>Aggregator</c> is cleared and
/// is reported as a "Test Assembly Cleanup Failure" if it holds an exception, which is what makes a failed
/// ADR-0039 retention check fail the run and <c>dotnet test</c> exit non-zero (the summary line may still say
/// "Passed" - the cleanup failure is reported separately). Both hold under <c>--filter</c>, so a single class
/// boots, sweeps and verifies exactly as a full run does.
/// </remarks>
public class AspireE2ETestAssemblyRunner(
    ITestAssembly testAssembly,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageSink executionMessageSink,
    ITestFrameworkExecutionOptions executionOptions)
    : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
{
    /// <inheritdoc/>
    protected override async Task AfterTestAssemblyStartingAsync()
    {
        await base.AfterTestAssemblyStartingAsync();
        await AspireE2EStack.BootAsync();
    }

    /// <inheritdoc/>
    protected override async Task BeforeTestAssemblyFinishedAsync()
    {
        await Aggregator.RunAsync(AspireE2EStack.ShutdownAsync);
        await base.BeforeTestAssemblyFinishedAsync();
    }
}
