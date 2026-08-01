using Xunit;

// SignInShould and DashboardShould both temporarily set the process-wide ConnectionStrings__blobs
// environment variable around building a WebApplicationFactory host (see their header comments) - disabling
// parallelization keeps that safe without needing collection-fixture ceremony for a project this small.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
