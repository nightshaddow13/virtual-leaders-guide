using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

public class SlugShould
{
    [Theory]
    [InlineData("Fall Retreat", "fall-retreat")]
    [InlineData("FALL RETREAT", "fall-retreat")]
    [InlineData("Fall   Retreat", "fall-retreat")]
    [InlineData("Café", "cafe")]
    [InlineData("Rock & Roll!", "rock-roll")]
    [InlineData("!!!Hello", "hello")]
    [InlineData("Hello!!!", "hello")]
    [InlineData("already-a-slug", "already-a-slug")]
    public void ProduceExpectedSlug_WhenGivenAName_ForFrom(string name, string expected)
    {
        Assert.Equal(expected, Slug.From(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void ReturnEmptyString_WhenNothingSurvives_ForFrom(string name)
    {
        Assert.Equal(string.Empty, Slug.From(name));
    }

    [Fact]
    public void TruncateToColumnLength_WhenTheDerivedSlugIsTooLong_ForFrom()
    {
        string longName = string.Join(' ', Enumerable.Repeat("word", 50));

        string slug = Slug.From(longName);

        Assert.True(slug.Length <= 100);
        Assert.NotEqual('-', slug[^1]);
    }
}
