namespace VirtualLeadersGuide.Web.Activities;

/// <summary>What the Activity page holds that isn't saved yet, and so is lost along with a deleted Activity.</summary>
/// <param name="Placements">How many pending placement rows there are.</param>
/// <param name="Details">Whether the Name or Description has been edited.</param>
internal readonly record struct UnsavedWork(int Placements = 0, bool Details = false);
