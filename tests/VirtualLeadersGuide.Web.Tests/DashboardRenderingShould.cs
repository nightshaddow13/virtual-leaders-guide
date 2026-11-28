using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Time;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>Dashboard.razor</c>'s own logic that <see cref="DashboardShould"/>/
/// <see cref="DashboardWithRoleClaimShould"/> don't reach - those two drive the page over real HTTP to prove
/// the no-role-redirect/renders-normally behavior at the prerender boundary (see their own remarks for why),
/// not the grid's data loading or the Admin-vs-Director markup difference, neither of which is observable
/// without an interactive render.
/// </remarks>
/// <remarks>
/// Status coverage (P2-20, #115): the header caption reads plain "@totalCount events", not
/// "ALL EVENTS · @totalCount" - once the default view is filtered to Current (Draft + not-yet-elapsed Live,
/// not literally everything), a static "ALL EVENTS" label would be actively wrong the moment a Past/Cancelled
/// Event exists.
/// </remarks>
/// <remarks>
/// Duplicate coverage (P2-21, #116, ADR-0054): <c>NavigateToNewEventWithTheSourcesDates_...</c> computes its
/// expected escaped query values independently from <see cref="EventResource"/>'s own fixture dates, rather
/// than calling <c>BuildDuplicateUrl</c> itself - that's deliberate, not an oversight to simplify later:
/// calling the same production helper the assertion is checking would make the test tautological.
/// </remarks>
/// <remarks>
/// Name search (P2.1-8, #205): the older-answers-last test holds the "sp" response back until "spl" has been
/// answered, the order a slow first request produces.
/// </remarks>
public class DashboardRenderingShould : BunitContext
{
    /// <remarks>
    /// Radzen components call into JS (e.g. <c>RadzenDataGrid</c>'s <c>Radzen.createDataGrid</c> on first
    /// render) for concerns this test has no stake in - sizing, virtualization. Loose mode returns a
    /// default for any unconfigured call instead of throwing, rather than hand-configuring every Radzen JS
    /// interop call this test doesn't actually exercise. <c>ApiDirectorClient</c> is registered here (not just
    /// where a test actually clicks Delete) because <c>Dashboard.razor.cs</c> injects it unconditionally now
    /// (P2-17, #112) - every render needs it resolvable even when a test never exercises the delete path.
    /// </remarks>
    public DashboardRenderingShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<BrowserTimeZoneAccessor>();
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        Services.AddSingleton(ApiClientTestFactory.CreateDirectorClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
    }

    [Fact]
    public void RedirectToNoAccess_WhenTheSignedInUserHoldsNoRoleClaim_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("user-1");

        Render<Dashboard>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("Account/NoAccess", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowNewEventButton_WhenTheSignedInUserIsAnAdmin_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = Array.Empty<object>() })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("+ New event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HideNewEventButton_WhenTheSignedInUserIsADirector_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = Array.Empty<object>() })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("+ New event", StringComparison.Ordinal));
        Assert.Contains("My events", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDeleteIcon_WhenTheSignedInUserIsAnAdmin_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Delete");
    }

    [Fact]
    public void HideDeleteIcon_WhenTheSignedInUserIsADirector_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.DoesNotContain(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Delete");
    }

    [Fact]
    public void ShowDuplicateIcon_WhenTheSignedInUserIsAnAdmin_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Duplicate");
    }

    [Fact]
    public void HideDuplicateIcon_WhenTheSignedInUserIsADirector_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.DoesNotContain(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Duplicate");
    }

    [Fact]
    public void NavigateToNewEventWithTheSourcesDates_WhenDuplicateIconIsClicked_ForBuildDuplicateUrl()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();
        IElement duplicateButton = cut.FindAll("button")
            .Single(button => button.GetAttribute("aria-label") == "Duplicate");
        duplicateButton.Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        string expectedStartsAt = Uri.EscapeDataString(new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero).ToString("o"));
        string expectedEndsAt = Uri.EscapeDataString(new DateTimeOffset(2026, 6, 14, 17, 0, 0, TimeSpan.Zero).ToString("o"));
        Assert.Contains($"dashboard/events/new?startsAt={expectedStartsAt}&endsAt={expectedEndsAt}", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigateToNewEventWithNoDateParameters_WhenDuplicatingAnEventWithNoDatesSet_ForBuildDuplicateUrl()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new
            {
                data = new[]
                {
                    new
                    {
                        type = "events",
                        id = Guid.NewGuid().ToString(),
                        attributes = new
                        {
                            name = "Undated Event",
                            slug = "undated-event",
                            passcode = "TigerLantern",
                            status = "Draft",
                            startsAt = (DateTimeOffset?)null,
                            endsAt = (DateTimeOffset?)null
                        }
                    }
                }
            })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();
        IElement duplicateButton = cut.FindAll("button")
            .Single(button => button.GetAttribute("aria-label") == "Duplicate");
        duplicateButton.Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("dashboard/events/new", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnInlineError_WhenTheEventStoreIsUnavailable_ForLoadDataAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"))));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("Something went wrong loading Events", cut.Markup, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Under <see cref="JSRuntimeMode.Loose"/> the timezone call resolves to <see cref="TimeZoneInfo.Utc"/>
    /// (<c>BrowserTimeZoneAccessor</c>'s own remarks) - real per-viewer conversion is
    /// <see cref="EventDateRangeShould"/>'s job. This only pins that the DATES column actually renders
    /// <see cref="EventDto.StartsAt"/>/<see cref="EventDto.EndsAt"/> through <c>FormatDates</c>, not a blank
    /// cell or the raw property.
    /// </remarks>
    [Fact]
    public void ShowTheFormattedDateRange_WhenAnEventHasBothDatesSet_ForLoadDataAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("JUN 12", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowABlankDatesCell_WhenAnEventHasNoDatesSet_ForLoadDataAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new
            {
                data = new[]
                {
                    new
                    {
                        type = "events",
                        id = Guid.NewGuid().ToString(),
                        attributes = new
                        {
                            name = "Undated Event",
                            slug = "undated-event",
                            passcode = "TigerLantern",
                            status = "Draft",
                            startsAt = (DateTimeOffset?)null,
                            endsAt = (DateTimeOffset?)null
                        }
                    }
                }
            })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        IElement datesCell = cut.FindAll("tbody tr").First().QuerySelectorAll("td")[2];
        Assert.Equal(string.Empty, datesCell.TextContent.Trim());
    }

    [Fact]
    public void RenderAStatusBadge_WhenAnEventIsLive_ForLoadDataAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Summer Camporee", "summer-camporee", "Live") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("LIVE", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAStatusBadge_WhenAnEventIsCancelled_ForLoadDataAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee", "Cancelled") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("CANCELLED", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowThePlainEventCount_WithNoStatusWord_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee") }, meta = new { total = 1 } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("1 events", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ALL EVENTS", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Director")]
    public void PutOpenGuideFirstInTheActionGroup_WhenTheViewerIsStaff_ForLoadDataAsync(string role)
    {
        IRenderedComponent<Dashboard> cut = RenderWithOneLiveEvent(role);

        IElement actionGroup = cut.Find("tbody tr td:last-child .rz-stack");

        Assert.Equal("Open guide", actionGroup.Children[0].GetAttribute("aria-label"));
    }

    [Fact]
    public void ShowTheDescriptionIconLabelledViewDetails_WhenTheSignedInUserIsADirector_ForLoadDataAsync()
    {
        IRenderedComponent<Dashboard> cut = RenderWithOneLiveEvent("Director");

        IElement details = cut.Find("button[aria-label='View details']");

        Assert.Equal("description", details.TextContent.Trim());
        Assert.Single(cut.FindAll("tbody i"), icon => icon.TextContent.Trim() == "visibility");
    }

    [Fact]
    public void KeepTheEditIcon_WhenTheSignedInUserIsAnAdmin_ForLoadDataAsync()
    {
        IRenderedComponent<Dashboard> cut = RenderWithOneLiveEvent("Admin");

        Assert.Equal("edit", cut.Find("button[aria-label='Edit']").TextContent.Trim());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Director")]
    public void OfferTheNameSearch_WhenTheViewerIsStaff_ForRender(string role)
    {
        IRenderedComponent<Dashboard> cut = RenderWithOneLiveEvent(role);

        Assert.NotNull(cut.Find("input[aria-label='Search by name']"));
    }

    [Fact]
    public async Task ReloadTheFirstPageWithTheNameFilter_WhenTheSearchChanges_ForOnNameSearchChangedAsync()
    {
        var queries = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new[] { EventResource("Fall Camporee", "fall-camporee") },
                    meta = new { total = 25 }
                })
            };
        });
        IRenderedComponent<Dashboard> cut = RenderAsAdmin(handler);
        await cut.InvokeAsync(() => cut.FindComponent<RadzenDataGrid<EventDto>>().Instance.GoToPage(1, true));
        Assert.Contains("page[number]=2", queries[^1], StringComparison.Ordinal);

        await SearchAsync(cut, "camp");

        Assert.Contains("page[number]=1&", queries[^1], StringComparison.Ordinal);
        Assert.Contains("filter=contains(name,'camp')", queries[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NameTheSearchTextInTheEmptyGrid_WhenNothingMatches_ForEmptyText()
    {
        IRenderedComponent<Dashboard> cut = RenderAsAdmin(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Assert.Contains("No Events yet.", cut.Markup, StringComparison.Ordinal);

        await SearchAsync(cut, "zzz");

        Assert.Contains("No Events match “zzz”.", cut.Find("tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepTheNewestResults_WhenAnOlderSearchAnswersLast_ForLoadDataAsync()
    {
        var older = new TaskCompletionSource<HttpResponseMessage>();
        IRenderedComponent<Dashboard> cut = RenderAsAdmin(new DeferringHandler(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            if (query.Contains("'sp')", StringComparison.Ordinal))
            {
                return older.Task;
            }

            return Task.FromResult(EventsResponse(
                query.Contains("'spl')", StringComparison.Ordinal) ? "Splashoree" : "Fall Camporee"));
        }));

        Task olderSearch = SearchAsync(cut, "sp");
        await SearchAsync(cut, "spl");
        older.SetResult(EventsResponse("Spring Fling"));
        await olderSearch;

        Assert.Contains("Splashoree", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Spring Fling", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadWithoutTheNameFilter_WhenTheSearchIsCleared_ForOnNameSearchChangedAsync()
    {
        var queries = new List<string>();
        IRenderedComponent<Dashboard> cut = RenderAsAdmin(new StubHttpMessageHandler(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            queries.Add(query);
            return EventsResponse(query.Contains("filter=", StringComparison.Ordinal) ? "Splashoree" : "Fall Camporee");
        }));
        await SearchAsync(cut, "spl");

        await SearchAsync(cut, "");

        Assert.DoesNotContain("filter=", queries[^1], StringComparison.Ordinal);
        Assert.Contains("Fall Camporee", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotClaimNoMatch_WhileTheSearchIsStillUnanswered_ForEmptyText()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>();
        IRenderedComponent<Dashboard> cut = RenderAsAdmin(new DeferringHandler(request =>
            request.RequestUri!.Query.Contains("filter=", StringComparison.Ordinal)
                ? answer.Task
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { data = Array.Empty<object>() })
                })));

        Task search = SearchAsync(cut, "zzz");
        cut.Render();

        Assert.DoesNotContain("No Events match", cut.Markup, StringComparison.Ordinal);
        answer.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data = Array.Empty<object>() })
        });
        await search;
        Assert.Contains("No Events match “zzz”.", cut.Markup, StringComparison.Ordinal);
    }

    private static HttpResponseMessage EventsResponse(string name) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { data = new[] { EventResource(name, "slug") } })
    };

    private static Task SearchAsync(IRenderedComponent<Dashboard> cut, string text) =>
        cut.InvokeAsync(() => cut.FindComponent<SearchBox>().Instance.SearchChanged.InvokeAsync(text));

    private IRenderedComponent<Dashboard> RenderAsAdmin(HttpMessageHandler handler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(handler));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        return Render<Dashboard>();
    }

    private sealed class DeferringHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => responder(request);
    }

    private IRenderedComponent<Dashboard> RenderWithOneLiveEvent(string role)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = new[] { EventResource("Fall Camporee", "fall-camporee", "Live") } })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("user-1");
        auth.SetRoles(role);

        return Render<Dashboard>();
    }

    private static object EventResource(string name, string slug, string status = "Draft") => new
    {
        type = "events",
        id = Guid.NewGuid().ToString(),
        attributes = new
        {
            name,
            slug,
            passcode = "TigerLantern",
            status,
            startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2026, 6, 14, 17, 0, 0, TimeSpan.Zero)
        }
    };
}
