namespace VirtualLeadersGuide.E2E.Tests;

// Every E2E test class must join this exact collection (ADR-0025's Consequences) - a second
// [CollectionDefinition] anywhere in this project would silently boot a second full Aspire stack alongside
// this one, doubling container/process startup cost and very likely deadlocking on AppHost.cs's fixed
// launch-profile ports and persistent SQL data volume.
[CollectionDefinition(nameof(AspireE2ECollection))]
public class AspireE2ECollection : ICollectionFixture<AspireE2EFixture>;
