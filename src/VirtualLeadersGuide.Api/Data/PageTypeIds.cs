namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// The well-known <see cref="PageType.Id"/> value, seeded via <c>HasData</c> in
/// <see cref="VirtualLeadersGuideDbContext"/> so <see cref="InfoPage.Create"/> can assign
/// <see cref="Page.PageTypeId"/> without a lookup.
/// </summary>
/// <remarks>
/// Lives here in <c>Api.Data</c>, not <c>VirtualLeadersGuide.Identity.Contracts</c> the way
/// <see cref="RoleIds"/> does - nothing outside Api needs a <see cref="PageType"/> id (a JSON:API client never
/// sets one; the endpoint it POSTs to already implies the type), so this doesn't need the cross-project
/// visibility <see cref="RoleIds"/> was placed there for.
/// </remarks>
public static class PageTypeIds
{
    public const int InfoPage = 1;
}
