using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers reordering from <c>ActivityDetail</c> (P5-13, #98) against a stateful fake Api that applies each PATCH
/// as a move, as the real one does (ADR-0079). Morning holds Archery, this Activity ("Canoe Basics") and Swim
/// Check, in that order.
/// </remarks>
public class ActivityDetailReorderShould : BunitContext
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _activityId = Guid.NewGuid();
    private readonly Guid _archeryId = Guid.NewGuid();
    private readonly Guid _swimId = Guid.NewGuid();
    private readonly Guid _morning = Guid.NewGuid();
    private readonly Guid _thisPlacement = Guid.NewGuid();

    private readonly List<(Guid Id, Guid ActivityId)> _bucket = [];
    private readonly List<(string Id, int SortOrder)> _patched = [];
    private HttpStatusCode _patchStatus = HttpStatusCode.NoContent;

    public ActivityDetailReorderShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<MarkdownRenderer>();
        _bucket.AddRange([(Guid.NewGuid(), _archeryId), (_thisPlacement, _activityId), (Guid.NewGuid(), _swimId)]);
    }

    [Theory]
    [InlineData("up", 0, "Moved to position 1 of 3.")]
    [InlineData("down", 2, "Moved to position 3 of 3.")]
    public void PatchTheNewPositionAndAnnounceIt_WhenAnArrowIsClicked_ForMoveAsync(string direction, int sent, string announced)
    {
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        Arrow(cut, direction).Click();

        cut.WaitForAssertion(() => Assert.Equal(announced, cut.Find("[role='status']").TextContent), Patience);
        Assert.Equal([(_thisPlacement.ToString(), sent)], _patched);
        Assert.True(Arrow(cut, direction).HasAttribute("disabled"));
        cut.WaitForAssertion(() => Assert.NotEmpty(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]), Patience);
    }

    [Fact]
    public void ShowThePermissionLostBannerAndDisableTheArrows_WhenTheMoveIsForbidden_ForMoveAsync()
    {
        _patchStatus = HttpStatusCode.Forbidden;
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        Arrow(cut, "down").Click();

        cut.WaitForAssertion(() => Assert.Contains("You no longer have permission", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.True(Arrow(cut, "up").HasAttribute("disabled"));
        Assert.True(Arrow(cut, "down").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "That placement changed. The tree was refreshed.")]
    [InlineData(HttpStatusCode.InternalServerError, "Couldn&#x27;t reorder that placement. Try again.")]
    public void ShowAnErrorAndAnnounceNothing_WhenTheMoveFails_ForMoveAsync(HttpStatusCode status, string message)
    {
        _patchStatus = status;
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        Arrow(cut, "down").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            WebUtility.HtmlDecode(message), WebUtility.HtmlDecode(cut.Markup), StringComparison.Ordinal), Patience);
        Assert.Empty(cut.Find("[role='status']").TextContent);
    }

    // --- helpers ----------------------------------------------------------------------------------------

    private static AngleSharp.Dom.IElement Arrow(IRenderedComponent<ActivityDetail> cut, string direction) =>
        cut.Find($"button[aria-label='Move Morning {direction}']");

    private IRenderedComponent<ActivityDetail> RenderPage()
    {
        RegisterServices();
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        return Render<ActivityDetail>(parameters => parameters
            .Add(c => c.EventId, _eventId)
            .Add(c => c.ActivityId, _activityId));
    }

    private void RegisterServices()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new
            {
                data = new
                {
                    type = "events", id = _eventId.ToString(),
                    attributes = new
                    {
                        name = "Fall Camporee", slug = "fall-camporee", passcode = "TigerLantern", status = "Draft",
                        startsAt = (DateTimeOffset?)null, endsAt = (DateTimeOffset?)null
                    }
                }
            })));

        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(new StubHttpMessageHandler(request =>
        {
            object Activity(Guid id, string name) => new
            {
                type = "activities", id = id.ToString(), attributes = new { eventId = _eventId, name, description = "content" }
            };

            return request.RequestUri!.AbsolutePath.EndsWith(_activityId.ToString(), StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = Activity(_activityId, "Canoe Basics") }) }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        data = new[] { Activity(_activityId, "Canoe Basics"), Activity(_archeryId, "Archery"), Activity(_swimId, "Swim Check") },
                        meta = new { total = 3 }
                    })
                };
        })));

        Services.AddSingleton(ApiClientTestFactory.CreatePlacementClient(new StubHttpMessageHandler(Respond)));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Patch)
        {
            string id = path[(path.LastIndexOf('/') + 1)..];
            using JsonDocument body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
            int position = body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("sortOrder").GetInt32();
            _patched.Add((id, position));
            if (_patchStatus == HttpStatusCode.NoContent)
            {
                (Guid Id, Guid ActivityId) row = _bucket.Single(p => p.Id.ToString() == id);
                _bucket.Remove(row);
                _bucket.Insert(position, row);
            }

            return new HttpResponseMessage(_patchStatus);
        }

        object[] data = path switch
        {
            "/api/tabs" => [new { type = "tabs", id = _morning.ToString(), attributes = new { eventId = _eventId, name = "Morning", sortOrder = 0 } }],
            "/api/placements" => [.. _bucket.Select((p, index) => new
            {
                type = "placements", id = p.Id.ToString(),
                attributes = new { eventId = _eventId, activityId = p.ActivityId, tabId = _morning, sortOrder = index }
            })],
            _ => []
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data, meta = new { total = data.Length } })
        };
    }
}
