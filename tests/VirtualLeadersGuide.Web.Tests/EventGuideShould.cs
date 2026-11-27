using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;
using VirtualLeadersGuide.Web.PublicGuide;
using static VirtualLeadersGuide.Web.Tests.GuideBuilder;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// See <see cref="HomeShould"/>'s class remarks for why <c>Change()</c>/<c>Submit()</c> against the rendered
/// Locked-state form reaches <c>UnlockAsync</c> despite ADR-0041's <c>[SupplyParameterFromForm]</c> gap.
/// </remarks>
/// <remarks>
/// Cancelled is unconditional, checked before the staff bypass - confirmed with the user as deliberate
/// (<c>EventGuide.razor.cs</c>'s own remarks): an Admin sees the same dark state a visitor does.
/// </remarks>
/// <remarks>
/// P5-14 (#88): the Unlocked state's guide content. Sanitization itself is not re-tested here -
/// <c>MarkdownRendererShould</c> owns it; these tests only confirm the rendered output actually reaches the
/// page.
/// </remarks>
public class EventGuideShould : BunitContext
{
    private static readonly Guid EventId = Guid.NewGuid();

    public EventGuideShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ShowNotFound_WhenTheLookupReturnsNotFound_ForLoadAsync()
    {
        RegisterServices(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("We couldn't find an event at that address", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnavailable_WhenTheLookupThrows_ForLoadAsync()
    {
        RegisterServices(_ => throw new HttpRequestException("simulated Api outage"));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("Something went wrong", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowCancelled_WhenTheEventIsCancelled_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto(status: "Cancelled")));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("This event was cancelled", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowCancelled_EvenForASignedInAdmin_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto(status: "Cancelled")));
        BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("This event was cancelled", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowLocked_WhenTheVisitorHasNoUnlockAndIsntStaff_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("Unlock this guide", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnlocked_WhenASignedInAdminVisits_WithNoCookieAtAll_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()));
        BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.NotNull(cut.Find(".vlg-guide-heading"));
    }

    [Fact]
    public void ShowUnlocked_WhenADirectorAssignedToThisEventVisits_WithNoCookieAtAll_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()));
        BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetClaims(new Claim(ClaimTypes.Role, RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = EventId })));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.NotNull(cut.Find(".vlg-guide-heading"));
    }

    [Fact]
    public void ShowLocked_WhenADirectorAssignedToADifferentEventVisits_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()));
        BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetClaims(new Claim(ClaimTypes.Role, RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid() })));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("Unlock this guide", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnlocked_WhenTheVisitorAlreadyHasAValidCookie_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()));
        var writeContext = new DefaultHttpContext();
        Services.GetRequiredService<PasscodeUnlockCookie>()
            .Unlock(writeContext, EventId, passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext);

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters(readContext));

        Assert.NotNull(cut.Find(".vlg-guide-heading"));
    }

    [Fact]
    public void ShowLocked_WhenTheCookiesVersionIsStale_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto(passcodeVersion: 2)));
        var writeContext = new DefaultHttpContext();
        Services.GetRequiredService<PasscodeUnlockCookie>()
            .Unlock(writeContext, EventId, passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext);

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters(readContext));

        Assert.Contains("Unlock this guide", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnInlineError_AndStayLocked_WhenThePasscodeIsWrong_ForUnlockAsync()
    {
        RegisterServices(request => request.RequestUri!.AbsolutePath.EndsWith("/passcode", StringComparison.Ordinal)
            ? JsonResponse(new PasscodeCheckResult { Matched = false })
            : JsonResponse(EventDto()));

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());
        cut.Find("#Input\\.Passcode").Change("WrongGuess");
        cut.Find("form").Submit();

        Assert.Contains("doesn't match this event", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Unlock this guide", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockInPlace_WhenThePasscodeMatches_ForUnlockAsync()
    {
        RegisterServices(request => request.RequestUri!.AbsolutePath.EndsWith("/passcode", StringComparison.Ordinal)
            ? JsonResponse(new PasscodeCheckResult { Matched = true, EventId = EventId, PasscodeVersion = 1 })
            : JsonResponse(EventDto()));

        var httpContext = new DefaultHttpContext();
        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters(httpContext));
        cut.Find("#Input\\.Passcode").Change("TigerLantern");
        cut.Find("form").Submit();

        Assert.NotNull(cut.Find(".vlg-guide-heading"));
        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void ShowTheFirstTabsScreen_WhenUnlockedWithNoQueryString_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()), TwoTabGuide());

        IRenderedComponent<EventGuide> cut = RenderAlreadyUnlocked();

        Assert.Contains("Opening", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Bring boots", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".rz-alert"));
    }

    [Fact]
    public void ShowTheRequestedScreen_WhenTheQueryStringNamesIt_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()), TwoTabGuide());
        NavigateTo("e/summer-camporee?tab=Packing");

        IRenderedComponent<EventGuide> cut = RenderAlreadyUnlocked();

        Assert.Contains("Bring boots", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Opening", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SayWhereTheVisitorIs_WhenOnASubTab_ForLoadAsync()
    {
        RegisterServices(
            _ => JsonResponse(EventDto()), Guide(Tab("Day", Screen("Opening"), SubTab("Round Robin", Screen("Rotation")))));
        NavigateTo("e/summer-camporee?tab=Day&sub=Round%20Robin");

        IRenderedComponent<EventGuide> cut = RenderAlreadyUnlocked();

        Assert.Equal("Day › Round Robin", cut.Find(".vlg-guide-location").TextContent.Trim());
    }

    [Fact]
    public void ShowANoticeAndAFallbackScreen_WhenTheQueryStringIsStale_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()), TwoTabGuide());
        NavigateTo("e/summer-camporee?tab=Evening");

        IRenderedComponent<EventGuide> cut = RenderAlreadyUnlocked();

        Assert.Contains("available anymore", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Opening", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheEmptyGuideMessage_WhenNothingIsPlaced_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()));

        IRenderedComponent<EventGuide> cut = RenderAlreadyUnlocked();

        Assert.Contains("haven't been added yet", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("nav"));
    }

    [Fact]
    public void ShowTheSameGuide_WhenASignedInAdminReachesUnlockedViaTheStaffBypass_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()), TwoTabGuide());
        BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        Assert.Contains("Opening", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheGuide_WhenThePasscodeMatches_ForUnlockAsync()
    {
        RegisterServices(
            request => request.Method == HttpMethod.Post
                ? JsonResponse(new PasscodeCheckResult { Matched = true, EventId = EventId, PasscodeVersion = 1 })
                : JsonResponse(EventDto()),
            TwoTabGuide());
        NavigateTo("e/summer-camporee?tab=Packing");
        IRenderedComponent<EventGuide> cut = Render<EventGuide>(Parameters());

        cut.Find("#Input\\.Passcode").Change("TigerLantern");
        cut.Find("form").Submit();

        Assert.Contains("Bring boots", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnInlineErrorButStayUnlocked_WhenTheGuideFetchFails_ForLoadAsync()
    {
        RegisterServices(_ => JsonResponse(EventDto()), guideFails: true);

        IRenderedComponent<EventGuide> cut = RenderAlreadyUnlocked();

        Assert.NotNull(cut.Find(".vlg-guide-heading"));
        Assert.Contains("didn't load", cut.Markup, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Intercepts the guide call before it ever reaches <paramref name="responder"/>, so a responder only has to
    /// handle the event-lookup/passcode paths. The guide defaults to one with no Tabs.
    /// </remarks>
    private void RegisterServices(
        Func<HttpRequestMessage, HttpResponseMessage> responder, PublicGuideDto? guide = null, bool guideFails = false)
    {
        PublicGuideDto guideResponse = guide ?? Guide();
        Services.AddSingleton(new PublicEventClient(new StubHttpClientFactory(new StubHttpMessageHandler(request =>
            !request.RequestUri!.AbsolutePath.EndsWith("/guide", StringComparison.Ordinal)
                ? responder(request)
                : guideFails
                    ? throw new HttpRequestException("simulated Api outage")
                    : JsonResponse(guideResponse)))));
        Services.AddSingleton(new PasscodeUnlockCookie(DataProtectionProvider.Create("VirtualLeadersGuide.Web.Tests")));
        Services.AddSingleton(new MarkdownRenderer());
    }

    private static PublicGuideDto TwoTabGuide() => Guide(
        Tab("Day", Screen("Opening")),
        Tab("Packing", InfoPageScreen("Packing List", "Bring boots.")));

    private void NavigateTo(string uri) => Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

    /// <remarks>
    /// Same cookie setup as <see cref="ShowUnlocked_WhenTheVisitorAlreadyHasAValidCookie_ForLoadAsync"/> -
    /// call after <see cref="RegisterServices"/> so <see cref="PasscodeUnlockCookie"/> is already registered.
    /// </remarks>
    private IRenderedComponent<EventGuide> RenderAlreadyUnlocked()
    {
        var writeContext = new DefaultHttpContext();
        Services.GetRequiredService<PasscodeUnlockCookie>()
            .Unlock(writeContext, EventId, passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext);

        return Render<EventGuide>(Parameters(readContext));
    }

    private static Action<ComponentParameterCollectionBuilder<EventGuide>> Parameters(HttpContext? httpContext = null) =>
        parameters => parameters
            .Add(c => c.Slug, "summer-camporee")
            .AddCascadingValue(httpContext ?? new DefaultHttpContext());

    private static HttpResponseMessage JsonResponse<T>(T body) =>
        new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(body) };

    private static PublicEventDto EventDto(string status = "Live", int passcodeVersion = 1) => new()
    {
        Id = EventId,
        Name = "Summer Camporee",
        Slug = "summer-camporee",
        Status = status,
        PasscodeVersion = passcodeVersion
    };

    /// <remarks>See <c>PasscodeUnlockCookieShould</c>'s identically-named helper.</remarks>
    private static HttpContext ReadingContextAfter(DefaultHttpContext writeContext)
    {
        var readContext = new DefaultHttpContext();
        var pairs = new List<string>();

        foreach (var parsed in Microsoft.Net.Http.Headers.SetCookieHeaderValue.ParseList(
            [.. writeContext.Response.Headers.SetCookie.OfType<string>()]))
        {
            pairs.Add($"{parsed.Name}={parsed.Value}");
        }

        readContext.Request.Headers.Cookie = string.Join("; ", pairs);
        return readContext;
    }
}
