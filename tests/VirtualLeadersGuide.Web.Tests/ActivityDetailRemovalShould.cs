using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers removing a Placement from <c>ActivityDetail.razor.cs</c> (P5-12, #97): the plain remove with an
/// Undo toast, the reap confirm, the last-Placement notice, ghost rows, and a refused delete. Separate from
/// <see cref="ActivityDetailShould"/> because it needs a stateful fake Api - one that serves Placements with
/// ids and records DELETE/POST/PATCH - where that suite's static tree is Tab-only. The fixture: this Activity
/// ("Canoe Basics") sits at Morning and Afternoon, and "Swim Check" shares both Tabs, so removing either row
/// leaves its Tab standing; tests that need a reap or a last row reshape <see cref="_placements"/> first.
/// </remarks>
public class ActivityDetailRemovalShould : BunitContext
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _activityId = Guid.NewGuid();
    private readonly Guid _otherActivityId = Guid.NewGuid();
    private readonly Guid _morning = Guid.NewGuid();
    private readonly Guid _afternoon = Guid.NewGuid();
    private readonly Guid _morningPlacement = Guid.NewGuid();
    private readonly Guid _afternoonPlacement = Guid.NewGuid();

    private readonly List<(Guid Id, Guid ActivityId, Guid TabId, int SortOrder)> _placements = [];
    private readonly List<string> _deleted = [];
    private readonly List<string> _posted = [];
    private readonly List<string> _patched = [];
    private HttpStatusCode _deleteStatus = HttpStatusCode.NoContent;

    public ActivityDetailRemovalShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _placements.AddRange(
        [
            (_morningPlacement, _activityId, _morning, 4),
            (_afternoonPlacement, _activityId, _afternoon, 0),
            (Guid.NewGuid(), _otherActivityId, _morning, 0),
            (Guid.NewGuid(), _otherActivityId, _afternoon, 0)
        ]);
    }

    [Fact]
    public void DeleteWithoutADialogAndOfferUndoInAToast_WhenTheTiersSurvive_ForRemoveAsync()
    {
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        RemoveButton(cut, "Morning").Click();

        cut.WaitForAssertion(() => Assert.Equal([_morningPlacement.ToString()], _deleted), Patience);
        cut.WaitForAssertion(() => Assert.Contains("Removed from Morning", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("Undo", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove this placement?", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RePostThePathAndRestoreItsSortOrder_WhenUndoIsClicked_ForUndoRemovalAsync()
    {
        IRenderedComponent<ActivityDetail> cut = RenderPage();
        RemoveButton(cut, "Morning").Click();
        cut.WaitForAssertion(() => Assert.Contains("Undo", cut.Markup, StringComparison.Ordinal), Patience);

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Undo").Click();

        cut.WaitForAssertion(() => Assert.Single(_posted), Patience);
        using (JsonDocument body = JsonDocument.Parse(_posted[0]))
        {
            Assert.Equal("Morning", body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("tabName").GetString());
        }

        cut.WaitForAssertion(() => Assert.Single(_patched), Patience);
        using JsonDocument patch = JsonDocument.Parse(_patched[0]);
        Assert.Equal(4, patch.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public void ConfirmFirstAndSendNothing_WhenTheUserKeepsARowThatWouldReapATier_ForRemoveAsync()
    {
        _placements.RemoveAll(p => p.ActivityId == _otherActivityId && p.TabId == _morning);
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        RemoveButton(cut, "Morning").Click();
        cut.WaitForAssertion(() => Assert.Contains("Remove this placement?", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("This is the only activity in Morning, so the tab disappears from the guide too.", cut.Markup, StringComparison.Ordinal);
        Services.GetRequiredService<DialogService>().Close(false);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Remove this placement?", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(_deleted);
    }

    [Fact]
    public void DeleteAndSkipTheUndoToast_WhenTheUserConfirmsARowThatReapsATier_ForRemoveAsync()
    {
        _placements.RemoveAll(p => p.ActivityId == _otherActivityId && p.TabId == _morning);
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        RemoveButton(cut, "Morning").Click();
        cut.WaitForAssertion(() => Assert.Contains("Remove this placement?", cut.Markup, StringComparison.Ordinal), Patience);
        Services.GetRequiredService<DialogService>().Close(true);

        cut.WaitForAssertion(() => Assert.Equal([_morningPlacement.ToString()], _deleted), Patience);
        Assert.DoesNotContain("Removed from", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainInsteadOfDeleting_WhenItIsTheActivitysOnlySavedPlacement_ForRemoveAsync()
    {
        _placements.RemoveAll(p => p.Id == _afternoonPlacement);
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        RemoveButton(cut, "Morning").Click();

        cut.WaitForAssertion(() => Assert.Contains("An activity needs at least one placement.", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(_deleted);
        Assert.DoesNotContain("Save your pending placements first", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTheSaveFirstHint_WhenTheOnlySavedRowHasPendingGhosts_ForRemoveAsync()
    {
        _placements.RemoveAll(p => p.Id == _afternoonPlacement);
        IRenderedComponent<ActivityDetail> cut = RenderPage();
        cut.Find("#PlacementTab").Change("Evening");
        cut.Find(".pb-place").Click();

        RemoveButton(cut, "Morning").Click();

        cut.WaitForAssertion(() => Assert.Contains("Save your pending placements first, then remove this one.", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(_deleted);
    }

    [Fact]
    public void DropTheGhostWithNoApiCall_WhenAPendingRowIsRemoved_ForRemoveAsync()
    {
        IRenderedComponent<ActivityDetail> cut = RenderPage();
        cut.Find("#PlacementTab").Change("Evening");
        cut.Find(".pb-place").Click();
        Assert.NotEmpty(cut.FindAll(".ptn-pending"));

        RemoveButton(cut, "Evening").Click();

        Assert.Empty(cut.FindAll(".ptn-pending"));
        Assert.Empty(_deleted);
        Assert.Empty(_posted);
    }

    [Fact]
    public void ShowDenied_WhenTheDeleteIsForbidden_ForRemoveAsync()
    {
        _deleteStatus = HttpStatusCode.Forbidden;
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        RemoveButton(cut, "Morning").Click();

        cut.WaitForAssertion(() => Assert.Contains("You don't have access to this Activity", cut.Markup, StringComparison.Ordinal), Patience);
    }

    /// <remarks>AC5: a Director on an Event they aren't assigned to. Api answers the DELETE with 403 (covered in <c>PlacementsResourceShould</c>); the page must show the denial, not an error or a stale tree.</remarks>
    [Fact]
    public void ShowDenied_WhenADirectorsDeleteIsForbidden_ForRemoveAsync()
    {
        _deleteStatus = HttpStatusCode.Forbidden;
        IRenderedComponent<ActivityDetail> cut = RenderPage("Director");

        RemoveButton(cut, "Morning").Click();

        cut.WaitForAssertion(() => Assert.Contains("You don't have access to this Activity", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Equal([_morningPlacement.ToString()], _deleted);
    }

    [Fact]
    public void TellThemItWasAlreadyRemoved_WhenApiSaysThePlacementIsGone_ForRemoveAsync()
    {
        _deleteStatus = HttpStatusCode.NotFound;
        IRenderedComponent<ActivityDetail> cut = RenderPage();

        RemoveButton(cut, "Morning").Click();

        cut.WaitForAssertion(() => Assert.Contains("That placement was already removed.", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.DoesNotContain("Removed from", cut.Markup, StringComparison.Ordinal);
    }

    // --- helpers ----------------------------------------------------------------------------------------

    private static AngleSharp.Dom.IElement RemoveButton(IRenderedComponent<ActivityDetail> cut, string path) =>
        cut.Find($"button[aria-label='Remove placement {path}']");

    private IRenderedComponent<ActivityDetail> RenderPage(string role = "Admin")
    {
        RegisterServices();
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized($"{role.ToLowerInvariant()}-1");
        auth.SetRoles(role);

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
                        data = new[] { Activity(_activityId, "Canoe Basics"), Activity(_otherActivityId, "Swim Check") },
                        meta = new { total = 2 }
                    })
                };
        })));

        Services.AddSingleton(ApiClientTestFactory.CreatePlacementClient(new StubHttpMessageHandler(Respond)));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Delete)
        {
            string id = path[(path.LastIndexOf('/') + 1)..];
            _deleted.Add(id);
            if (_deleteStatus == HttpStatusCode.NoContent)
            {
                _placements.RemoveAll(p => p.Id.ToString() == id);
            }

            return new HttpResponseMessage(_deleteStatus);
        }

        if (request.Method == HttpMethod.Patch)
        {
            _patched.Add(request.Content!.ReadAsStringAsync().Result);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == HttpMethod.Post)
        {
            _posted.Add(request.Content!.ReadAsStringAsync().Result);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        type = "placements", id = Guid.NewGuid().ToString(),
                        attributes = new { eventId = _eventId, activityId = _activityId, tabId = _morning, sortOrder = 0 }
                    }
                })
            };
        }

        object[] data = path switch
        {
            "/api/tabs" => [
                new { type = "tabs", id = _morning.ToString(), attributes = new { eventId = _eventId, name = "Morning", sortOrder = 0 } },
                new { type = "tabs", id = _afternoon.ToString(), attributes = new { eventId = _eventId, name = "Afternoon", sortOrder = 1 } }],
            "/api/placements" => [.. _placements.Select(p => new
            {
                type = "placements", id = p.Id.ToString(),
                attributes = new { eventId = _eventId, activityId = p.ActivityId, tabId = p.TabId, sortOrder = p.SortOrder }
            })],
            _ => []
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data, meta = new { total = data.Length } })
        };
    }
}
