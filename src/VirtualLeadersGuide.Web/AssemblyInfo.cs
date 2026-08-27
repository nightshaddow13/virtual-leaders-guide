using System.Runtime.CompilerServices;

// Every class this test project has referenced so far (ApiUserStore, AdminAllowlistSynchronizer, etc.) is
// public - this project had no InternalsVisibleTo before. The Identity pages' bUnit tests (ADR-0041) are
// the first to need one of this assembly's internal-sealed types directly: IdentityRedirectManager, which
// every one of them injects and which has no public constructor a test could otherwise reach.
[assembly: InternalsVisibleTo("VirtualLeadersGuide.Web.Tests")]
