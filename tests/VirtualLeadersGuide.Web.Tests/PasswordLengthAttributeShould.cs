using System.ComponentModel.DataAnnotations;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class PasswordLengthAttributeShould
{
    private static readonly PasswordLengthAttribute Attribute = new();

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("12345")]
    public void RejectTooShort_WhenUnderSixCharacters_ForIsValid(string password) =>
        Assert.False(Attribute.IsValid(password));

    [Fact]
    public void RejectTooLong_WhenOverOneHundredCharacters_ForIsValid() =>
        Assert.False(Attribute.IsValid(new string('a', 101)));

    [Theory]
    [InlineData("123456")]
    [InlineData("a-perfectly-ordinary-password")]
    public void AcceptInRange_WhenBetweenSixAndOneHundredCharacters_ForIsValid(string password) =>
        Assert.True(Attribute.IsValid(password));

    [Fact]
    public void AcceptExactlySixCharacters_AtTheMinimumBoundary_ForIsValid() =>
        Assert.True(Attribute.IsValid(new string('a', 6)));

    [Fact]
    public void AcceptExactlyOneHundredCharacters_AtTheMaximumBoundary_ForIsValid() =>
        Assert.True(Attribute.IsValid(new string('a', 100)));

    [Fact]
    public void AcceptNonStringValues_WhenValueIsNull_ForIsValid() =>
        Assert.True(Attribute.IsValid(null));
}
