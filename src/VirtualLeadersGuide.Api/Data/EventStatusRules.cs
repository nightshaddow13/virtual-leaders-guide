namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// The in-memory (as opposed to query-expression) form of "is this Live row actually Past" - the one place
/// that rule is computed outside a query, shared by <see cref="EventResourceDefinition"/> and
/// <see cref="PublicGuide.PublicGuideEndpoints"/> so it can't drift into a third copy (P4-2, #72).
/// </summary>
/// <remarks>
/// Extracted from <see cref="EventResourceDefinition"/>, which originally kept this as a private static
/// method before <c>PublicGuideEndpoints</c> needed the identical rule for the single Event it loads by
/// Slug - a plain in-memory comparison, not a filter-expression tree the way
/// <see cref="EventStatusFilterRewriter"/>'s collection-level rewriting needs.
/// </remarks>
internal static class EventStatusRules
{
    /// <summary>Computes the effective <see cref="EventStatus"/> for a single already-loaded row.</summary>
    /// <param name="stored">The <see cref="Event.Status"/> value as persisted.</param>
    /// <param name="endsAt">The <see cref="Event.EndsAt"/> value as persisted.</param>
    /// <param name="now">The current instant - callers pass a single resolved "now" so every check in one request agrees.</param>
    /// <returns>
    /// <see cref="EventStatus.Past"/> when <paramref name="stored"/> is <see cref="EventStatus.Live"/> and
    /// <paramref name="endsAt"/> has elapsed; <paramref name="stored"/> unchanged otherwise.
    /// <paramref name="endsAt"/> is deliberately <see langword="null"/>-safe: a <see cref="EventStatus.Live"/>
    /// Event with no end date is never Past (CONTEXT.md's Starts at / Ends at entry - an unset date isn't an
    /// elapsed one).
    /// </returns>
    public static EventStatus EffectiveStatus(EventStatus stored, DateTimeOffset? endsAt, DateTimeOffset now) =>
        stored == EventStatus.Live && endsAt is { } ends && ends <= now ? EventStatus.Past : stored;
}
