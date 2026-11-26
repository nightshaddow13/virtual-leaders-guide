using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Account;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// A return URL comes from the query string, so anyone can write one. Each case here is judged by where a
/// browser would end up - the navigated value resolved against the site's base address - rather than by its
/// exact text.
/// </remarks>
public class IdentityRedirectManagerShould : BunitContext
{
    [Theory]
    [InlineData("//elsewhere.invalid/landing")]
    [InlineData("/\\elsewhere.invalid/landing")]
    [InlineData("\\\\elsewhere.invalid/landing")]
    [InlineData(" //elsewhere.invalid/landing")]
    [InlineData("https://elsewhere.invalid/landing")]
    [InlineData("http://localhost.elsewhere.invalid/landing")]
    [InlineData("http://localhost//elsewhere.invalid/landing")]
    [InlineData("http://localhost///elsewhere.invalid/landing")]
    [InlineData("/https://elsewhere.invalid/landing")]
    [InlineData("javascript:alert(1)")]
    public void StayOnThisSite_WhenTheUriPointsElsewhereOrIsDisguisedToLookLocal_ForRedirectTo(string uri)
    {
        Uri destination = DestinationOf(uri);

        Assert.Equal("localhost", destination.Host);
        Assert.Equal(Uri.UriSchemeHttp, destination.Scheme);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("dashboard", "/dashboard")]
    [InlineData("/Account/Manage", "/Account/Manage")]
    [InlineData("dashboard/events?page=2#top", "/dashboard/events?page=2#top")]
    [InlineData("http://localhost/dashboard", "/dashboard")]
    public void KeepThePathQueryAndFragment_WhenTheUriIsOnThisSite_ForRedirectTo(string? uri, string expected)
    {
        Uri destination = DestinationOf(uri);

        Assert.Equal(expected, destination.PathAndQuery + destination.Fragment);
    }

    private Uri DestinationOf(string? uri)
    {
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

        new IdentityRedirectManager(navigation).RedirectTo(uri);

        return new Uri(new Uri(navigation.BaseUri), navigation.History.First().Uri);
    }
}
