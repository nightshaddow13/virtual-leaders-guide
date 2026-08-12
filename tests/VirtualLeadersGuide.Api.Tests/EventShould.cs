using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

// Unit-level coverage of Event.Create in isolation (no DbContext/DB involved) - complements EventSchemaShould,
// which exercises Event against the real EF model/constraints. This is what actually proves P2-6's (#15) first
// AC bullet - "a URL-safe Slug is auto-derived from the Name as a starting value" - since nothing else in this
// ticket calls Slug.From at Event-construction time.
public class EventShould
{
    [Fact]
    public void DeriveSlugFromName_WhenSlugIsOmitted_ForCreate()
    {
        Event @event = Event.Create("Fall Retreat");

        Assert.Equal("fall-retreat", @event.Slug);
    }

    [Fact]
    public void UseTheGivenSlug_WhenSlugIsProvided_ForCreate()
    {
        Event @event = Event.Create("Fall Retreat", "custom-slug");

        Assert.Equal("custom-slug", @event.Slug);
    }

    [Fact]
    public void SetTheGivenName_ForCreate()
    {
        Event @event = Event.Create("Fall Retreat");

        Assert.Equal("Fall Retreat", @event.Name);
    }

    [Fact]
    public void GenerateANonBlankPasscode_ForCreate()
    {
        Event @event = Event.Create("Fall Retreat");

        Assert.False(string.IsNullOrWhiteSpace(@event.Passcode));
    }

    [Fact]
    public void AssignAFreshIdEachCall_ForCreate()
    {
        Event first = Event.Create("Fall Retreat");
        Event second = Event.Create("Fall Retreat 2");

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }
}
