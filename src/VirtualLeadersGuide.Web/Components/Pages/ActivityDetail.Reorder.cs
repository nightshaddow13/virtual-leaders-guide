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
    /// Writes immediately, like a removal, with no toast or Undo - the opposite arrow is the undo. The tree is
    /// reloaded even when the write threw, since Api may have applied it before the response was lost. Focus and
    /// the announcement are set only after the move is redrawn and the controls are enabled again, and only
    /// when both the write and the reload worked.
    /// </remarks>
    private async Task MoveAsync(PlacementMove move)
    {
        if (move.Leaf.PlacementId is not { } placementId || WriteBlocked || treeStale)
        {
            return;
        }

        saveErrorMessage = null;
        ClearMoveFeedback();
        var problems = new List<string>();
        isReordering = true;
        try
        {
            try
            {
                PlacementWriteOutcome outcome = await PlacementClient.MoveAsync(
                    placementId, move.Leaf.Position + (int)move.Direction, CancellationToken.None);
                if (outcome == PlacementWriteOutcome.Forbidden)
                {
                    permissionLost = true;
                    return;
                }

                if (outcome != PlacementWriteOutcome.Success)
                {
                    problems.Add("That placement changed. The tree was refreshed.");
                }
            }
            catch (ActivityDataUnavailableException)
            {
                problems.Add("Couldn't reorder that placement. Try again.");
            }

            if (!await TryRefreshTreeAsync())
            {
                problems.Add(RefreshFailedText);
            }
        }
        finally
        {
            isReordering = false;
        }

        if (problems.Count > 0)
        {
            saveErrorMessage = string.Join(" ", problems);
        }
        else if (model!.FindLeaf(placementId) is { } leaf)
        {
            focusRequest = new PlacementFocusRequest(placementId, move.Direction);
            announcement = $"Moved to position {leaf.Position + 1} of {leaf.SavedCount}.";
        }
    }

    /// <summary>Drops the focus request once an arrow has taken focus, so a row drawn again later doesn't take it back.</summary>
    private void OnMoveFocused() => focusRequest = null;

    /// <summary>Drops the last move's focus request and announcement, so a later redraw doesn't replay them.</summary>
    private void ClearMoveFeedback()
    {
        focusRequest = null;
        announcement = null;
    }
}
