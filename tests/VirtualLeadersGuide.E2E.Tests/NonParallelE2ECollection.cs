namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// A fixture-less xUnit collection whose only job is <c>DisableParallelization</c>: a class that joins it runs
/// alone, after every parallel class has finished.
/// </summary>
/// <remarks>
/// Holds <see cref="EventManagementScenarios"/> only, until P2.1-8 (#205) makes its Admin-grid row lookups
/// independent of paging - until then, other classes creating Events concurrently can push the row it is
/// looking for off the first page. It deliberately carries no <c>ICollectionFixture</c>: the one Aspire stack
/// is owned by <see cref="AspireE2EStack"/>, and a collection fixture here would boot a second one (ADR-0075).
/// </remarks>
[CollectionDefinition(nameof(NonParallelE2ECollection), DisableParallelization = true)]
public class NonParallelE2ECollection;
