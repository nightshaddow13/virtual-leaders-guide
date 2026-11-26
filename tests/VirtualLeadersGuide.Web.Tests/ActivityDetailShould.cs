using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>ActivityDetail.razor.cs</c> (P5-11, #96): its access gating, the ghost-row flow (place, save,
/// failure keeps the rest pending), the Name/Description half of the one Save (P5-23, #206) and the
/// leave-confirmation guard. The builder's and the model's own logic
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
    private HttpStatusCode _patchStatus = HttpStatusCode.NoContent;
    private readonly List<string> _patchedBodies = [];
    private readonly List<HttpMethod> _writes = [];
    private readonly Queue<HttpStatusCode> _postStatuses = new();
    private bool _emptyReadsAfterAForbiddenWrite;
    private bool _failReadsAfterAWrite;
    private TaskCompletionSource? _holdWrites;

    /// <remarks>Generous on purpose: the solution-level test run executes this project alongside the Api suite, and bUnit's 1 s default is too tight when the machine is busy.</remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public ActivityDetailShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<MarkdownRenderer>();
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
        Assert.DoesNotContain("Discard unsaved changes?", cut.Markup, StringComparison.Ordinal);
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
        cut.WaitForAssertion(() => Assert.Contains("Discard unsaved changes?", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("Evening", cut.Find(".vlg-consequence-list").TextContent, StringComparison.Ordinal);
        Services.GetRequiredService<DialogService>().Close(false);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Discard unsaved changes?", cut.Markup, StringComparison.Ordinal), Patience);
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
        cut.WaitForAssertion(() => Assert.Contains("Discard unsaved changes?", cut.Markup, StringComparison.Ordinal), Patience);
        Services.GetRequiredService<DialogService>().Close(true);

        Assert.True(
            SpinWait.SpinUntil(() => navigation.Uri.EndsWith($"dashboard/events/{_eventId}/activities", StringComparison.Ordinal), Patience),
            $"Still at {navigation.Uri}.");
    }

    [Fact]
    public void HydrateNameAndDescriptionWithSaveDisabled_WhenTheActivityLoads_ForOnParametersSetAsync()
    {
        Register();

        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        Assert.Equal("Canoe Basics", cut.Find("#Name").GetAttribute("value"));
        Assert.Equal("content", cut.Find("#Description").GetAttribute("value"));
        Assert.Equal("Edit activity", cut.Find("h1").TextContent);
        Assert.True(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void PatchOnlyAndShowTheNewName_WhenOnlyTheNameChanged_ForSaveAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        var navigation = Services.GetRequiredService<NavigationManager>();
        string before = navigation.Uri;
        cut.Find("#Name").Input("Canoe Basics II");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("CANOE BASICS II", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("\"name\":\"Canoe Basics II\"", Assert.Single(_patchedBodies), StringComparison.Ordinal);
        Assert.Empty(_postedBodies);
        Assert.True(SaveButton(cut).HasAttribute("disabled"));
        Assert.Equal(before, navigation.Uri);
    }

    [Fact]
    public void ShowAValidationMessageAndWriteNothing_WhenNameIsBlankWithAGhostPending_ForSubmitAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");
        cut.Find("#Name").Input("");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Enter a name.", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(_writes);
    }

    [Fact]
    public void PatchBeforePosting_WhenBothKindsOfChangeArePending_ForSaveAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        cut.Find("#Description").Input("Bring a jacket.");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal([HttpMethod.Patch, HttpMethod.Post], _writes), Patience);
        cut.WaitForAssertion(() => Assert.True(SaveButton(cut).HasAttribute("disabled")), Patience);
    }

    [Fact]
    public void SaveThePlacementAndKeepTheNameDirty_WhenThePatchFails_ForSaveAsync()
    {
        _patchStatus = HttpStatusCode.InternalServerError;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        cut.Find("#Name").Input("Canoe Basics II");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("save the Name and Description", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Single(_postedBodies);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".ptn-pending")), Patience);
        Assert.Equal("Canoe Basics II", cut.Find("#Name").GetAttribute("value"));
        Assert.DoesNotContain("CANOE BASICS II", cut.Markup, StringComparison.Ordinal);
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void SaveTheNameAndKeepTheGhostPending_WhenThePlacementIsRejected_ForSaveAsync()
    {
        _postStatus = HttpStatusCode.UnprocessableEntity;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        cut.Find("#Name").Input("Canoe Basics II");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Check its levels", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("CANOE BASICS II", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("save the Name and Description", cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".ptn-pending"));
    }

    [Fact]
    public void ShowThePermissionLostBannerAndSendNoPlacements_WhenThePatchIsForbidden_ForSaveAsync()
    {
        _patchStatus = HttpStatusCode.Forbidden;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Director");
        cut.Find("#Name").Input("Canoe Basics II");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("You no longer have permission", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Empty(_postedBodies);
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.True(SaveButton(cut).HasAttribute("disabled"));
        Assert.Equal("Canoe Basics II", cut.Find("#Name").GetAttribute("value"));
    }

    [Fact]
    public void ShowThePermissionLostBannerNotTheDeniedPanel_WhenAPlacementPostIsForbidden_ForSaveAsync()
    {
        _postStatus = HttpStatusCode.Forbidden;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Director");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("You no longer have permission", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.DoesNotContain("You don't have access", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    }

    [Fact]
    public void KeepTheSavedNameInTheTree_WhileTheNameFieldIsDirty_ForRender()
    {
        Guid tabId = Guid.NewGuid();
        Register(tree: new Tree(Tabs: [(tabId, "Morning")], PlacedTabIds: [tabId]));
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");

        cut.Find("#Name").Input("Canoe Basics II");

        Assert.Equal("Canoe Basics", cut.Find(".ptn-lit").TextContent.Trim());
    }

    [Fact]
    public void PromptAboutTheDetails_WhenLeavingWithADirtyNameAndNoGhosts_ForConfirmLeavingAsync()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        cut.Find("#Name").Input("Canoe Basics II");

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click();

        cut.WaitForAssertion(() => Assert.Contains("Discard unsaved changes?", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("Name/Description changes", cut.Find(".vlg-consequence-list").TextContent, StringComparison.Ordinal);
        Services.GetRequiredService<DialogService>().Close(false);
    }

    [Fact]
    public void KeepTheTreeAsItWas_WhenASaveIsForbidden_ForSaveAsync()
    {
        _patchStatus = HttpStatusCode.Forbidden;
        _emptyReadsAfterAForbiddenWrite = true;
        Guid tabId = Guid.NewGuid();
        Register(tree: new Tree(Tabs: [(tabId, "Morning")], PlacedTabIds: [tabId]));
        IRenderedComponent<ActivityDetail> cut = RenderPage("Director");
        cut.Find("#Name").Input("Canoe Basics II");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("You no longer have permission", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Equal("Canoe Basics", cut.Find(".ptn-lit").TextContent.Trim());
        Assert.DoesNotContain("Not placed anywhere yet", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NameWhatDidSave_WhenALaterPlacementIsRejected_ForSaveAsync()
    {
        _postStatuses.Enqueue(HttpStatusCode.Created);
        _postStatuses.Enqueue(HttpStatusCode.UnprocessableEntity);
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");
        PlaceTab(cut, "Night");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Saved: Evening.", cut.Markup, StringComparison.Ordinal), Patience);
        Assert.Contains("Check its levels", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SayTheTreeMayBeOutOfDate_WhenTheReloadFailsAfterASave_ForSaveAsync()
    {
        _failReadsAfterAWrite = true;
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        PlaceTab(cut, "Evening");

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("the tree may be out of date", cut.Markup, StringComparison.Ordinal), Patience);
    }

    [Fact]
    public void AskTheBrowserToConfirmLeaving_OnlyWhileSomethingIsUnsaved_ForRender()
    {
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        Assert.False(cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);

        cut.Find("#Name").Input("Canoe Basics II");

        Assert.True(cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
    }

    [Fact]
    public void PromptAndSayASaveIsRunning_WhenLeavingMidSave_ForConfirmLeavingAsync()
    {
        _holdWrites = new TaskCompletionSource();
        Register();
        IRenderedComponent<ActivityDetail> cut = RenderPage("Admin");
        cut.Find("#Name").Input("Canoe Basics II");
        SaveButton(cut).Click();
        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.InvokeAsync(() => navigation.NavigateTo("dashboard"));

        cut.WaitForAssertion(() => Assert.Contains("A save is still running", cut.Markup, StringComparison.Ordinal), Patience);
        Services.GetRequiredService<DialogService>().Close(false);
        _holdWrites.SetResult();
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

        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(activityHandler ?? new HeldHandler(() => _holdWrites?.Task, request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                _writes.Add(request.Method);
                _patchedBodies.Add(request.Content!.ReadAsStringAsync().Result);
                return new HttpResponseMessage(_patchStatus);
            }

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
                if (_postStatuses.TryDequeue(out HttpStatusCode queued))
                {
                    _postStatus = queued;
                }

                _writes.Add(request.Method);
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

            if (_failReadsAfterAWrite && _writes.Count > 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            // What Api really does for a Director who has lost the Event: the reads succeed and come back empty.
            bool accessGone = _emptyReadsAfterAForbiddenWrite && _writes.Count > 0;
            return Collection(request.RequestUri!.AbsolutePath, accessGone ? null : tree);
        })));

        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    /// <summary>Answers like <see cref="StubHttpMessageHandler"/>, but keeps a write waiting while <paramref name="hold"/> returns a task, so a test can act mid-save.</summary>
    private sealed class HeldHandler(Func<Task?> hold, Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get && hold() is { } release)
            {
                await release;
            }

            return respond(request);
        }
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
