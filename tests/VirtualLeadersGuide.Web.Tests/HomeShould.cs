using System.Net;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Drives the actual <c>EditForm</c> POST via <c>Change()</c>/<c>Submit()</c> against the rendered inputs,
/// the same technique <see cref="LoginShould"/> already established - <c>Home.razor.cs</c>'s own
/// <c>Input ??= new();</c> means the ADR-0041 gap ("a private <c>[SupplyParameterFromForm]</c> can't be set
/// from a component test") doesn't block this the way it first looked like it would: bUnit never needs to
/// name the property at all, it just fills in and submits the rendered form like a browser would.
/// </remarks>
public class HomeShould : BunitContext
{
    public HomeShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void RenderTheEntryForm_WithNoError_ForInitialRender()
    {
        RegisterEventClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        IRenderedComponent<Home> cut = Render<Home>(Parameters());

        Assert.Contains("Find your event", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("rz-alert-danger", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowNotFound_WhenTheAddressMatchesNoEvent_ForFindEventAsync()
    {
        RegisterEventClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        IRenderedComponent<Home> cut = Render<Home>(Parameters());
        Submit(cut, "no-such-event", "TigerLantern");

        Assert.Contains("We couldn't find an event at that address", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RedirectWithoutCheckingThePasscode_WhenTheEventIsCancelled_ForFindEventAsync()
    {
        var passcodeChecked = false;
        RegisterEventClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/passcode", StringComparison.Ordinal))
            {
                passcodeChecked = true;
            }

            return JsonResponse(HttpStatusCode.OK, EventDto(status: "Cancelled"));
        });

        IRenderedComponent<Home> cut = Render<Home>(Parameters());
        Submit(cut, "summer-camporee", "anything");

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("e/summer-camporee", navigation.History.Last().Uri, StringComparison.Ordinal);
        Assert.False(passcodeChecked);
    }

    [Fact]
    public void ShowAnInlineError_WhenThePasscodeIsWrong_ForFindEventAsync()
    {
        RegisterEventClient(request => request.RequestUri!.AbsolutePath.EndsWith("/passcode", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, new PasscodeCheckResult { Matched = false })
            : JsonResponse(HttpStatusCode.OK, EventDto()));

        IRenderedComponent<Home> cut = Render<Home>(Parameters());
        Submit(cut, "summer-camporee", "WrongGuess");

        Assert.Contains("doesn't match this event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockAndRedirect_WhenThePasscodeMatches_ForFindEventAsync()
    {
        var eventId = Guid.NewGuid();
        RegisterEventClient(request => request.RequestUri!.AbsolutePath.EndsWith("/passcode", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK,
                new PasscodeCheckResult { Matched = true, EventId = eventId, PasscodeVersion = 1 })
            : JsonResponse(HttpStatusCode.OK, EventDto(eventId)));

        var httpContext = new DefaultHttpContext();
        IRenderedComponent<Home> cut = Render<Home>(Parameters(httpContext));
        Submit(cut, "summer-camporee", "TigerLantern");

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("e/summer-camporee", navigation.History.Last().Uri, StringComparison.Ordinal);
        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private void RegisterEventClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        Services.AddSingleton(new PublicEventClient(new StubHttpClientFactory(new StubHttpMessageHandler(responder))));
        Services.AddSingleton(new PasscodeUnlockCookie(DataProtectionProvider.Create("VirtualLeadersGuide.Web.Tests")));
    }

    private static Action<ComponentParameterCollectionBuilder<Home>> Parameters(DefaultHttpContext? httpContext = null) =>
        parameters => parameters.AddCascadingValue(httpContext ?? new DefaultHttpContext { Request = { Method = "POST" } });

    private static void Submit(IRenderedComponent<Home> cut, string address, string passcode)
    {
        cut.Find("#Input\\.Address").Change(address);
        cut.Find("#Input\\.Passcode").Change(passcode);
        cut.Find("form").Submit();
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) =>
        new(status) { Content = System.Net.Http.Json.JsonContent.Create(body) };

    private static PublicEventDto EventDto(Guid? id = null, string status = "Live") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Summer Camporee",
        Slug = "summer-camporee",
        Status = status,
        PasscodeVersion = 1
    };
}
