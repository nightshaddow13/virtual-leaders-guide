using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>A JSON:API resource object carrying <typeparamref name="TAttributes"/> - wire-format detail for the Tier/Placement resources, <see langword="internal"/> to this feature area like <c>ActivityResourceObject</c>.</summary>
internal sealed class TierResourceObject<TAttributes>
{
    public required string Type { get; init; }

    public string? Id { get; init; }

    public TAttributes? Attributes { get; init; }
}

/// <summary>The response body for a <c>GET</c> on any of the Tier/Placement collections.</summary>
internal sealed class TierCollectionDocument<TAttributes>
{
    public required List<TierResourceObject<TAttributes>> Data { get; init; }

    public DocumentMeta? Meta { get; init; }
}

/// <summary>A single-resource document - the request body for a Placement POST and its response.</summary>
internal sealed class TierDocument<TAttributes>
{
    public required TierResourceObject<TAttributes> Data { get; init; }
}

/// <summary>Attributes shared by <c>/api/tabs</c>, <c>/api/subTabs</c>, <c>/api/sections</c> and <c>/api/subSections</c> - each only populates the parent id it has.</summary>
internal sealed class TierAttributesDto
{
    public string? Name { get; init; }

    public int? SortOrder { get; init; }

    public Guid? TabId { get; init; }

    public Guid? ParentTabId { get; init; }

    public Guid? ParentSubTabId { get; init; }

    public Guid? SectionId { get; init; }
}

/// <summary>A Placement's attributes, as read back (resolved ids) and as sent on create (names for Tiers that may not exist yet - ADR-0072).</summary>
internal sealed class PlacementAttributesDto
{
    public Guid? ActivityId { get; init; }

    public Guid? TabId { get; init; }

    public Guid? SubTabId { get; init; }

    public Guid? SectionId { get; init; }

    public Guid? SubSectionId { get; init; }

    public string? TabName { get; init; }

    public string? SubTabName { get; init; }

    public string? SectionName { get; init; }

    public string? SubSectionName { get; init; }

    public int? SortOrder { get; init; }
}

/// <summary>An InfoPage Placement's attributes, as read back (resolved ids) and as sent on create or move (names - ADR-0072).</summary>
internal sealed class InfoPagePlacementAttributesDto
{
    /// <summary>The InfoPage being placed - sent on create only.</summary>
    public Guid? InfoPageId { get; init; }

    /// <summary>The resolved Tab, as read back.</summary>
    public Guid? TabId { get; init; }

    /// <summary>The resolved Sub Tab, as read back.</summary>
    public Guid? SubTabId { get; init; }

    /// <summary>The Tab's name, as sent on create or move.</summary>
    public string? TabName { get; init; }

    /// <summary>The Sub Tab's name, as sent on create or move.</summary>
    public string? SubTabName { get; init; }
}
