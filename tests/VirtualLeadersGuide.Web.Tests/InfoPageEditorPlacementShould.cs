using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>Covers the InfoPage editor's Placement section (P5-24, #208).</remarks>
public class InfoPageEditorPlacementShould : BunitContext
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _infoPageId = Guid.NewGuid();
    private readonly Guid _tabId = Guid.NewGuid();
    private readonly Guid _placementId = Guid.NewGuid();
    private readonly List<(HttpMethod Method, string Body)> _writes = [];
    private bool _placed;
    private bool _activitiesOnTab;
    private HttpStatusCode _writeStatus = HttpStatusCode.NoContent;
    private string? _errorCode;

    public InfoPageEditorPlacementShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<MarkdownRenderer>();
    }

    [Fact]
    public void ShowAHintAndNoFields_WhenTheInfoPageIsNew_ForRender()
    {
        IRenderedComponent<InfoPageEditor> cut = RenderEditor(isNew: true);

        Assert.Contains("You can place this page once it", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#PlacementTab"));
    }

    [Fact]
    public void ShowThePicker_WhenTheInfoPageIsNotPlaced_ForRender()
    {
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();

        Assert.NotEmpty(cut.FindAll("#PlacementTab"));
    }

    [Fact]
    public void ShowWhereItIsPlacedInsteadOfThePicker_WhenPlaced_ForRender()
    {
        _placed = true;

        IRenderedComponent<InfoPageEditor> cut = RenderEditor();

        Assert.Contains("Placed on Logistics", cut.Find(".ipe-placed").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#PlacementTab"));
    }

    [Fact]
    public void PostTheTypedTabAndLeave_WhenSaving_ForUpdateAsync()
    {
        _writeStatus = HttpStatusCode.Created;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();
        cut.Find("#PlacementTab").Change("About");

        Click(cut, "Save changes");

        (HttpMethod method, string body) = Assert.Single(_writes);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Contains("\"tabName\":\"About\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/info-pages", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteNoPlacement_WhenNoTabIsPicked_ForUpdateAsync()
    {
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();

        Click(cut, "Save changes");

        Assert.Empty(_writes);
    }

    [Fact]
    public void PatchTheExistingPlacement_WhenChangedAndSaved_ForUpdateAsync()
    {
        _placed = true;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();
        Click(cut, "Change");
        cut.Find("#PlacementTab").Change("About");

        Click(cut, "Save changes");

        Assert.Equal(HttpMethod.Patch, Assert.Single(_writes).Method);
    }

    [Theory]
    [InlineData("slot-has-activities", "already shows Activities")]
    [InlineData("slot-has-other-infopage", "already shows another Info page")]
    public void StayAndSayWhy_WhenTheScreenIsTaken_ForUpdateAsync(string code, string expected)
    {
        _writeStatus = HttpStatusCode.Conflict;
        _errorCode = code;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();
        string before = Services.GetRequiredService<NavigationManager>().Uri;
        cut.Find("#PlacementTab").Change("Logistics");

        Click(cut, "Save changes");

        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Equal(before, Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void LosePermission_WhenThePlacementWriteIsForbidden_ForUpdateAsync()
    {
        _writeStatus = HttpStatusCode.Forbidden;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();
        cut.Find("#PlacementTab").Change("About");

        Click(cut, "Save changes");

        cut.WaitForAssertion(
            () => Assert.Contains("You no longer have permission", cut.Markup, StringComparison.Ordinal), Patience);
    }

    [Fact]
    public void ConfirmNamingTheReapedTierThenDelete_WhenRemoving_ForRemovePlacementAsync()
    {
        _placed = true;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();

        Click(cut, "Remove");
        cut.WaitForAssertion(() => Assert.Contains("the tab disappears", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(_writes);
        Services.GetRequiredService<DialogService>().Close(true);

        cut.WaitForAssertion(() => Assert.Equal(HttpMethod.Delete, Assert.Single(_writes).Method), Patience);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#PlacementTab")), Patience);
    }

    [Fact]
    public void DeleteWithoutAsking_WhenNothingWouldBeReaped_ForRemovePlacementAsync()
    {
        _placed = true;
        _activitiesOnTab = true;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();

        Click(cut, "Remove");

        cut.WaitForAssertion(() => Assert.Equal(HttpMethod.Delete, Assert.Single(_writes).Method), Patience);
        cut.WaitForAssertion(() => Assert.Contains("Removed from Logistics", cut.Markup, StringComparison.Ordinal), Patience);
    }

    [Fact]
    public void PlaceItBackOnTheSamePath_WhenUndoIsClicked_ForUndoRemovalAsync()
    {
        _placed = true;
        _activitiesOnTab = true;
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();
        Click(cut, "Remove");
        cut.WaitForAssertion(() => Assert.Contains("Undo", cut.Markup, StringComparison.Ordinal), Patience);

        Click(cut, "Undo");

        cut.WaitForAssertion(() => Assert.Equal(2, _writes.Count), Patience);
        Assert.Equal(HttpMethod.Post, _writes[1].Method);
        Assert.Contains("\"tabName\":\"Logistics\"", _writes[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void DropTheUnsavedPick_WhenTheComponentIsReusedForAnotherRoute_ForOnParametersSetAsync()
    {
        IRenderedComponent<InfoPageEditor> cut = RenderEditor();
        cut.Find("#PlacementTab").Change("About");

        cut.Render(parameters => parameters.Add(c => c.InfoPageId, _infoPageId));

        cut.WaitForAssertion(
            () => Assert.True(string.IsNullOrEmpty(cut.Find("#PlacementTab").GetAttribute("value"))), Patience);
    }

    private static void Click(IRenderedComponent<InfoPageEditor> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    private IRenderedComponent<InfoPageEditor> RenderEditor(bool isNew = false)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new
        {
            data = new
            {
                type = "events", id = _eventId.ToString(),
                attributes = new { name = "Fall Camporee", slug = "fall-camporee", passcode = "TigerLantern", status = "Draft" }
            }
        })));

        Services.AddSingleton(ApiClientTestFactory.CreateInfoPageClient(new StubHttpMessageHandler(request =>
            request.Method == HttpMethod.Get
                ? Json(new
                {
                    data = new
                    {
                        type = "infoPages", id = _infoPageId.ToString(),
                        attributes = new { eventId = _eventId, title = "About", markdownContent = "content" }
                    }
                })
                : new HttpResponseMessage(HttpStatusCode.NoContent))));

        Services.AddSingleton(ApiClientTestFactory.CreatePlacementClient(
            new StubHttpMessageHandler(request => Collection(request.RequestUri!.AbsolutePath))));

        Services.AddSingleton(ApiClientTestFactory.CreateInfoPagePlacementClient(new StubHttpMessageHandler(request =>
        {
            _writes.Add((request.Method, request.Content?.ReadAsStringAsync().Result ?? ""));
            if (request.Method == HttpMethod.Delete)
            {
                _placed = false;
            }

            return new HttpResponseMessage(_writeStatus)
            {
                Content = JsonContent.Create(new { errors = new[] { new { code = _errorCode } } })
            };
        })));

        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        return Render<InfoPageEditor>(parameters =>
        {
            parameters.Add(c => c.EventId, _eventId);
            if (!isNew)
            {
                parameters.Add(c => c.InfoPageId, _infoPageId);
            }
        });
    }

    private HttpResponseMessage Collection(string path)
    {
        object[] data = path switch
        {
            "/api/tabs" =>
            [
                new { type = "tabs", id = _tabId.ToString(), attributes = new { name = "Logistics", sortOrder = 0 } }
            ],
            "/api/placements" when _activitiesOnTab =>
            [
                new { type = "placements", id = Guid.NewGuid().ToString(), attributes = new { activityId = Guid.NewGuid(), tabId = _tabId, sortOrder = 0 } }
            ],
            "/api/infoPagePlacements" when _placed =>
            [
                new { type = "infoPagePlacements", id = _placementId.ToString(), attributes = new { infoPageId = _infoPageId, tabId = _tabId } }
            ],
            _ => []
        };

        return Json(new { data });
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
