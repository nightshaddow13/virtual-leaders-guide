namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Marks the single xUnit collection every E2E test class must join to share one <see cref="AspireE2EFixture"/>
/// instance for the whole test run.
/// </summary>
// Every E2E test class must join this exact collection (ADR-0025's Consequences) - a second
// [CollectionDefinition] anywhere in this project would silently boot a second full Aspire stack alongside
// this one, doubling container/process startup cost and very likely deadlocking on AppHost.cs's fixed
// launch-profile ports and persistent SQL data volume.
[CollectionDefinition(nameof(AspireE2ECollection))]
public class AspireE2ECollection : ICollectionFixture<AspireE2EFixture>;
