using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Tests;

/// <summary>Shorthand for building a <see cref="PublicGuideDto"/> in the public guide's component and model tests.</summary>
internal static class GuideBuilder
{
    /// <summary>A guide holding <paramref name="tabs"/> in the given order.</summary>
    public static PublicGuideDto Guide(params PublicGuideTabDto[] tabs) => new() { Tabs = tabs };

    /// <summary>A Tab; its bare screen is empty unless <paramref name="screen"/> is given.</summary>
    public static PublicGuideTabDto Tab(string name, PublicGuideScreenDto? screen = null, params PublicGuideSubTabDto[] subTabs) =>
        new() { Name = name, Screen = screen ?? Screen(), SubTabs = subTabs };

    /// <summary>A Sub Tab; its screen is empty unless <paramref name="screen"/> is given.</summary>
    public static PublicGuideSubTabDto SubTab(string name, PublicGuideScreenDto? screen = null) =>
        new() { Name = name, Screen = screen ?? Screen() };

    /// <summary>A screen of directly placed Activities, one per name - empty when there are none.</summary>
    public static PublicGuideScreenDto Screen(params string[] activityNames) =>
        new() { Activities = [.. activityNames.Select(name => Activity(name))], Sections = [] };

    /// <summary>A screen holding one InfoPage.</summary>
    public static PublicGuideScreenDto InfoPageScreen(string title, string markdownContent) => new()
    {
        InfoPage = new PublicInfoPageDto { Id = Guid.NewGuid(), Title = title, MarkdownContent = markdownContent },
        Activities = [],
        Sections = []
    };

    /// <summary>An Activity with the given name and markdown description.</summary>
    public static PublicGuideActivityDto Activity(string name, string description = "") =>
        new() { Name = name, Description = description };
}
