namespace VirtualLeadersGuide.Web.Events;

/// <summary>The Dashboard's STATUS filter options - distinct from <see cref="EventStatus"/> itself.</summary>
/// <remarks>
/// Two extra options <see cref="EventStatus"/> doesn't have: <see cref="Current"/> (the default - Draft plus
/// not-yet-elapsed Live, meaning "send no <c>filter=</c> at all and let Api's own default collection view
/// apply") and <see cref="All"/> (every Status, sent as <c>any(status,'Draft','Live','Past','Cancelled')</c>).
/// Deliberately a separate type, not <c>EventStatus?</c> with <see langword="null"/> standing in for one of
/// these - overloading one nullable enum to mean two different "no specific status" ideas invites exactly the
/// confusion a dedicated type avoids: "Current" never secretly means "everything," so the option a viewer
/// picks can't be misread against what the code actually sends.
/// </remarks>
public enum EventStatusFilter
{
    Current,
    All,
    Draft,
    Live,
    Past,
    Cancelled
}
