using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>ActivityDetail.razor.cs</c> (P5-11, #96): its access gating, the ghost-row flow (place, save,
/// failure keeps the rest pending) and the leave-confirmation guard. The builder's and the model's own logic
/// have their own suites (<see cref="ActivityPlacementBuilderShould"/>, <see cref="PlacementTreeModelShould"/>);
/// here the autofill is driven only as far as typing a Tab. <c>JSInterop</c> is
/// <see cref="JSRuntimeMode.Loose"/> for <c>RadzenAutoComplete</c>'s popup call, as in
/// <see cref="FacilityEditorShould"/>. Waiting for a navigation to land polls <c>navigation.Uri</c> directly: bUnit's
/// <c>WaitForAssertion</c> re-checks only when the component re-renders, and a navigation renders nothing, so it
/// checked once and timed out whenever the handler's continuation had not finished by then.
/// </remarks>
public class ActivityDetailShould : BunitContext
{
    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _activityId = Guid.NewGuid();
    private readonly List<string> _postedBodies = [];
    private HttpStatusCode _postStatus = HttpStatusCode.Created;

    /// <remarks>Generous on purpose: the solution-level test run executes this project alongside the Api suite, and bUnit's 1 s default is too tight when the machine is busy.</remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public ActivityDetailShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShowDenied_WhenTheEventReadIsForbidden_ForOnParametersSetAsync()
    {
        Register(eventHandler: StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));

        IRenderedComponent<ActivityDetail> cut = RenderPage("Director");

        Assert.Contains("You don't have access to this Activity", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenTheActivityReadIsForbidden_ForOnParametersSetAsync()
    {
        Register(activityHandler: StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));

        IRenderedComponent<ActivityDetail> cut = RenderPage("Director");

        Assert.Contains("You don't have access to this Activity", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowNotFound_WhenTheActivityDoesNotExist_ForOnParametersSetAsync()
    {
        Register(activityHandler: StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));

        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        Assert.Contains("Activity not found", cut.Markup, StringComparison.Ordinal);
    }

    /// <remarks>A hand-edited URL pairing one Event's access with another Event's Activity must not render it.</remarks>
    [Fact]
    public void ShowNotFound_WhenTheActivityBelongsToADifferentEvent_ForOnParametersSetAsync()
    {
        Register(activityOwnerEventId: Guid.NewGuid());

        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        Assert.Contains("Activity not found", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnavailable_WhenAStoreThrows_ForOnParametersSetAsync()
    {
        Register(placementHandler: StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")));

        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        Assert.Contains("Something went wrong loading this page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheEmptyTreeAndKeepSaveDisabled_WhenTheActivityHasNoPlacements_ForOnParametersSetAsync()
    {
        Register();

        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        Assert.Contains("Not placed anywhere yet", cut.Markup, StringComparison.Ordinal);
        Assert.True(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void LightThisActivitysSavedRow_WhenItHasAPlacement_ForOnParametersSetAsync()
    {
        Guid tabId = Guid.NewGuid();
        Register(tree: new Tree(Tabs: [(tabId, "Morning")], PlacedTabIds: [tabId]));

        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        Assert.Equal("Canoe Basics", cut.Find(".ptn-lit").TextContent.Trim());
        Assert.Equal("1 SO FAR", cut.Find(".pb-count").TextContent.Trim());
    }

    [Fact]
    public void DrawADashedGhostRowAndEnableSave_WhenPlaceItIsClicked_ForOnPlace()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        PlaceTab(cut, "Evening");

        Assert.Contains("PENDING", cut.Find(".ptn-pending").TextContent, StringComparison.Ordinal);
        Assert.Contains("NEW", cut.Find(".ptn-tab").TextContent, StringComparison.Ordinal);
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
        Assert.Empty(_postedBodies);
    }

    [Fact]
    public void PostEachGhostByNameInOrderAndClearThem_WhenSaveIsClicked_ForSaveAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");
        PlaceTab(cut, "Night");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _postedBodies.Count), Patience);
        Assert.Equal("Evening", TabNameSent(_postedBodies[0]));
        Assert.Equal("Night", TabNameSent(_postedBodies[1]));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".ptn-pending")), Patience);
        Assert.True(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void KeepTheRejectedGhostPendingAndShowAnError_WhenApiRejectsThePath_ForSaveAsync()
    {
        _postStatus = HttpStatusCode.UnprocessableEntity;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Couldn't save", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.NotEmpty(cut.FindAll(".ptn-pending"));
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void TreatAConflictAsAlreadySaved_WhenApiReportsTheActivityIsAlreadyThere_ForSaveAsync()
    {
        _postStatus = HttpStatusCode.Conflict;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Already placed, so skipped: Evening", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(cut.FindAll(".ptn-pending"));
    }

    [Fact]
    public void NavigateWithoutPrompting_WhenNothingIsPending_ForConfirmLeavingAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click();

        Assert.EndsWith($"dashboard/events/{_eventId}/activities", navigation.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain("Discard unsaved placements?", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptAndStayPut_WhenLeavingWithGhostsAndTheUserKeepsEditing_ForConfirmLeavingAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");
        var navigation = Services.GetRequiredService<NavigationManager>();
        string before = navigation.Uri;

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click();
        cut.WaitForAssertion(() => Assert.Contains("Discard unsaved placements?", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("Evening", cut.Find(".vlg-consequence-list").TextContent, StringComparison.Ordinal);
        Services.GetRequiredService<DialogService>().Close(false);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Discard unsaved placements?", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Equal(before, navigation.Uri);
        Assert.NotEmpty(cut.FindAll(".ptn-pending"));
    }

    [Fact]
    public void PromptOnceAndLeave_WhenLeavingWithGhostsAndTheUserDiscards_ForConfirmLeavingAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");
        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click();
        cut.WaitForAssertion(() => Assert.Contains("Discard unsaved placements?", cut.Markup, StringComparison.Ordinal), Patience);
        Services.GetRequiredService<DialogService>().Close(true);

        Assert.True(
            SpinWait.SpinUntil(() => navigation.Uri.EndsWith($"dashboard/events/{_eventId}/activities", StringComparison.Ordinal), Patience),
            $"Still at {navigation.Uri}.");
    }

    // --- helpers ----------------------------------------------------------------------------------------

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<ActivityDetail> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Save changes");

    private static void PlaceTab(IRenderedComponent<ActivityDetail> cut, string tab)
    {
        cut.Find("#PlacementTab").Change(tab);
        cut.Find(".pb-place").Click();
    }

    private static string? TabNameSent(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("tabName").GetString();
    }

    private IRenderedComponent<ActivityDetail> RenderPage(string role)
    {
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized($"{role.ToLowerInvariant()}-1");
        auth.SetRoles(role);

        return Render<ActivityDetail>(parameters => parameters
            .Add(c => c.EventId, _eventId)
            .Add(c => c.ActivityId, _activityId));
    }

    /// <summary>The Event's Tiers and which Tabs this Activity is placed directly under - all a test here needs.</summary>
    private sealed record Tree(IReadOnlyList<(Guid Id, string Name)> Tabs, IReadOnlyList<Guid> PlacedTabIds);

    private void Register(
        HttpMessageHandler? eventHandler = null, HttpMessageHandler? activityHandler = null,
        HttpMessageHandler? placementHandler = null, Guid? activityOwnerEventId = null, Tree? tree = null)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler ?? StubHttpMessageHandler.RespondingWithJson(
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

        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(activityHandler ?? new StubHttpMessageHandler(request =>
        {
            var resource = new
            {
                type = "activities", id = _activityId.ToString(),
                attributes = new { eventId = activityOwnerEventId ?? _eventId, name = "Canoe Basics", description = "content" }
            };

            return request.RequestUri!.AbsolutePath.EndsWith(_activityId.ToString(), StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = resource }) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = new[] { resource }, meta = new { total = 1 } }) };
        })));

        Services.AddSingleton(ApiClientTestFactory.CreatePlacementClient(placementHandler ?? new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                _postedBodies.Add(request.Content!.ReadAsStringAsync().Result);
                return _postStatus == HttpStatusCode.Created
                    ? new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = JsonContent.Create(new
                        {
                            data = new
                            {
                                type = "placements", id = Guid.NewGuid().ToString(),
                                attributes = new { eventId = _eventId, activityId = _activityId, tabId = Guid.NewGuid(), sortOrder = 0 }
                            }
                        })
                    }
                    : new HttpResponseMessage(_postStatus)
                    {
                        Content = JsonContent.Create(new
                        {
                            errors = new[] { new { title = "x", source = new { pointer = "/data/attributes/tabId" } } }
                        })
                    };
            }

            return Collection(request.RequestUri!.AbsolutePath, tree);
        })));

        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private HttpResponseMessage Collection(string path, Tree? tree)
    {
        object[] data = path switch
        {
            "/api/tabs" => [.. (tree?.Tabs ?? []).Select((t, i) => new
            {
                type = "tabs", id = t.Id.ToString(), attributes = new { eventId = _eventId, name = t.Name, sortOrder = i }
            })],
            "/api/placements" => [.. (tree?.PlacedTabIds ?? []).Select(tabId => new
            {
                type = "placements", id = Guid.NewGuid().ToString(),
                attributes = new { eventId = _eventId, activityId = _activityId, tabId, sortOrder = 0 }
            })],
            _ => []
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data, meta = new { total = data.Length } })
        };
    }
}
