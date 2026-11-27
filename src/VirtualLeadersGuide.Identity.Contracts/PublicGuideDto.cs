using System.Text.Json.Serialization;

namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// An Event's whole public guide, returned by <c>PublicGuideRoutes.GuideByEvent</c> (P5-14, #88): every Tab and
/// what each screen under it holds.
/// </summary>
/// <remarks>
/// Every list arrives already in display order, so Web never sorts. No Tier ids: a screen is addressed by Tier
/// name (ADR-0080).
/// </remarks>
public sealed class PublicGuideDto
{
    /// <summary>The Event's Tabs, in display order.</summary>
    public required IReadOnlyList<PublicGuideTabDto> Tabs { get; set; }
}

/// <summary>One Tab: its own bare screen plus its Sub Tabs.</summary>
public sealed class PublicGuideTabDto
{
    /// <summary>The Tab's name.</summary>
    public required string Name { get; set; }

    /// <summary>The bare Tab's screen - what is placed on the Tab with no Sub Tab (ADR-0047).</summary>
    public required PublicGuideScreenDto Screen { get; set; }

    /// <summary>The Tab's Sub Tabs, in display order.</summary>
    public required IReadOnlyList<PublicGuideSubTabDto> SubTabs { get; set; }
}

/// <summary>One Sub Tab and its screen.</summary>
public sealed class PublicGuideSubTabDto
{
    /// <summary>The Sub Tab's name.</summary>
    public required string Name { get; set; }

    /// <summary>What is placed on this Sub Tab.</summary>
    public required PublicGuideScreenDto Screen { get; set; }
}

/// <summary>What one screen shows: either an InfoPage or Activities, never both (ADR-0047).</summary>
public sealed class PublicGuideScreenDto
{
    /// <summary>The screen's InfoPage; when set, <see cref="Activities"/> and <see cref="Sections"/> are empty.</summary>
    public PublicInfoPageDto? InfoPage { get; set; }

    /// <summary>Activities placed directly on the screen, above any Section.</summary>
    public required IReadOnlyList<PublicGuideActivityDto> Activities { get; set; }

    /// <summary>The screen's Sections, in display order.</summary>
    public required IReadOnlyList<PublicGuideSectionDto> Sections { get; set; }

    /// <summary>Whether there is nothing at all to show.</summary>
    [JsonIgnore]
    public bool IsEmpty => InfoPage is null && Activities.Count == 0 && Sections.Count == 0;
}

/// <summary>A Section heading with what sits under it.</summary>
public sealed class PublicGuideSectionDto
{
    /// <summary>The Section's name.</summary>
    public required string Name { get; set; }

    /// <summary>Activities placed directly in the Section.</summary>
    public required IReadOnlyList<PublicGuideActivityDto> Activities { get; set; }

    /// <summary>The Section's Sub Sections, in display order.</summary>
    public required IReadOnlyList<PublicGuideSubSectionDto> SubSections { get; set; }
}

/// <summary>A Sub Section heading with its Activities.</summary>
public sealed class PublicGuideSubSectionDto
{
    /// <summary>The Sub Section's name.</summary>
    public required string Name { get; set; }

    /// <summary>Activities placed in the Sub Section.</summary>
    public required IReadOnlyList<PublicGuideActivityDto> Activities { get; set; }
}

/// <summary>One Activity as a visitor reads it.</summary>
public sealed class PublicGuideActivityDto
{
    /// <summary>The Activity's name.</summary>
    public required string Name { get; set; }

    /// <summary>Raw markdown, never sanitized HTML - the same contract as <see cref="PublicInfoPageDto.MarkdownContent"/> (ADR-0048).</summary>
    public required string Description { get; set; }
}
