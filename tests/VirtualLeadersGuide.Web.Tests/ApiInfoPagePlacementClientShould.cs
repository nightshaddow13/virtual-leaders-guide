using System.Net;
using System.Text.Json;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>Covers <see cref="ApiInfoPagePlacementClient"/>'s request shapes and status/error-code mapping (P5-24, #208).</remarks>
public class ApiInfoPagePlacementClientShould
{
    [Fact]
    public async Task PostTheInfoPageAndTierNames_ForCreateAsync()
    {
        Guid infoPageId = Guid.NewGuid();
        HttpMethod? method = null;
        string body = "";
        var client = Client(request =>
        {
            method = request.Method;
            body = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        InfoPagePlacementWriteOutcome outcome = await client.CreateAsync(infoPageId, "Logistics", "Arrival", CancellationToken.None);

        Assert.Equal(InfoPagePlacementWriteOutcome.Success, outcome);
        Assert.Equal(HttpMethod.Post, method);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal(infoPageId, attributes.GetProperty("infoPageId").GetGuid());
        Assert.Equal("Logistics", attributes.GetProperty("tabName").GetString());
        Assert.Equal("Arrival", attributes.GetProperty("subTabName").GetString());
    }

    [Fact]
    public async Task PatchOnlyTheNewPath_ForMoveAsync()
    {
        Guid placementId = Guid.NewGuid();
        HttpRequestMessage? sent = null;
        string body = "";
        var client = Client(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await client.MoveAsync(placementId, "About", null, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, sent!.Method);
        Assert.EndsWith($"/api/infoPagePlacements/{placementId}", sent.RequestUri!.ToString(), StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal(["tabName"], attributes.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("slot-has-activities", InfoPagePlacementWriteOutcome.SlotHasActivities)]
    [InlineData("slot-has-other-infopage", InfoPagePlacementWriteOutcome.SlotHasOtherInfoPage)]
    [InlineData("infopage-already-placed", InfoPagePlacementWriteOutcome.AlreadyPlaced)]
    public async Task MapTheErrorCode_WhenApiRespondsWithConflict_ForCreateAsync(string code, InfoPagePlacementWriteOutcome expected)
    {
        ApiInfoPagePlacementClient client = ApiClientTestFactory.CreateInfoPagePlacementClient(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Conflict, new { errors = new[] { new { code } } }));

        Assert.Equal(expected, await client.CreateAsync(Guid.NewGuid(), "Logistics", null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowInfoPageDataUnavailableException_WhenAConflictBodyIsUnreadable_ForCreateAsync()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.Conflict));

        await Assert.ThrowsAsync<InfoPageDataUnavailableException>(
            () => client.CreateAsync(Guid.NewGuid(), "Logistics", null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, InfoPagePlacementWriteOutcome.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, InfoPagePlacementWriteOutcome.NotFound)]
    [InlineData(HttpStatusCode.NoContent, InfoPagePlacementWriteOutcome.Success)]
    public async Task MapTheStatus_ForDeleteAsync(HttpStatusCode status, InfoPagePlacementWriteOutcome expected)
    {
        var client = Client(_ => new HttpResponseMessage(status));

        Assert.Equal(expected, await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowInfoPageDataUnavailableException_WhenApiRespondsUnexpectedly_ForDeleteAsync()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<InfoPageDataUnavailableException>(
            () => client.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static ApiInfoPagePlacementClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        ApiClientTestFactory.CreateInfoPagePlacementClient(new StubHttpMessageHandler(respond));
}
