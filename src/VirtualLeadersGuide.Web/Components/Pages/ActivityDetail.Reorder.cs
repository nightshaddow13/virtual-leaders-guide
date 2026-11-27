using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// The reordering part of <see cref="ActivityDetail"/> (P5-13, #98): moving one of this Activity's saved rows
/// a step within its path.
/// </summary>
public partial class ActivityDetail
{
    private bool isReordering;
    private PlacementFocusRequest? focusRequest;
    private string? announcement;

    /// <remarks>
    /// Writes immediately, like a removal, with no toast or Undo - the opposite arrow is the undo. Focus and
    /// the announcement are set only after the move is redrawn and the controls are enabled again, and only
    /// when the reload worked.
    /// </remarks>
    private async Task MoveAsync(PlacementMove move)
    {
        if (move.Leaf.PlacementId is not { } placementId || WriteBlocked)
        {
            return;
        }

        saveErrorMessage = null;
        ClearMoveFeedback();
        bool moved = false;
        isReordering = true;
        try
        {
            switch (await PlacementClient.MoveAsync(placementId, move.Leaf.Position + move.Delta, CancellationToken.None))
            {
                case PlacementWriteOutcome.Forbidden:
                    permissionLost = true;
                    return;
                case PlacementWriteOutcome.Success:
                    moved = true;
                    break;
                default:
                    saveErrorMessage = "That placement changed. The tree was refreshed.";
                    break;
            }

            await RefreshOrReportAsync();
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = "Couldn't reorder that placement. Try again.";
        }
        finally
        {
            isReordering = false;
        }

        if (moved && saveErrorMessage is null && model!.FindLeaf(placementId) is { } leaf)
        {
            focusRequest = new PlacementFocusRequest(placementId, move.Delta);
            announcement = $"Moved to position {leaf.Position + 1} of {leaf.SavedCount}.";
        }
    }

    /// <summary>Drops the last move's focus request and announcement, so a later redraw doesn't replay them.</summary>
    private void ClearMoveFeedback()
    {
        focusRequest = null;
        announcement = null;
    }
}
