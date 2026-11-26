namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>The dashboard's view of an <c>/api/infoPagePlacements</c> row (P5-24, #208).</summary>
public sealed class InfoPagePlacementDto
{
    /// <summary>This Placement's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The InfoPage this Placement is for.</summary>
    public required Guid InfoPageId { get; init; }

    /// <summary>The Tab it is under.</summary>
    public required Guid TabId { get; init; }

    /// <summary>The Sub Tab it is under, or <see langword="null"/> when it fills the bare Tab's screen.</summary>
    public Guid? SubTabId { get; init; }
}
