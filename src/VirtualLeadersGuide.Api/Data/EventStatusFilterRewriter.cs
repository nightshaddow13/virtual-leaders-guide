using JsonApiDotNetCore.Queries.Expressions;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Rewrites a client-supplied <c>filter=</c> naming <see cref="Event.Status"/> into the effective-status form
/// <see cref="EventResourceDefinition.OnSerialize"/> would produce for the same rows, since
/// <see cref="EventStatus.Past"/> is never a stored value (ADR-0053) - <c>equals(status,'Past')</c> has to
/// become "Live and elapsed", not a literal column compare that could never match anything.
/// </summary>
/// <remarks>
/// Also the single place that detects whether a request's filter mentions <see cref="Event.Status"/> at all
/// (<see cref="ClientNamedStatus"/>) - <see cref="EventResourceDefinition.OnApplyFilter"/> uses that to decide
/// whether to AND in its own default hide of Cancelled/effectively-Past Events, so the two rules (rewrite a
/// named status; hide by default when none was named) share one walk of the expression tree instead of two.
/// <see cref="VisitResourceFieldChain"/> catches every other operator against Status (e.g. a hypothetical
/// <c>greaterThan(status, 'Live')</c>) so the suppression is complete even for a form this rewriter doesn't
/// specifically rewrite - those forms compare the raw stored value, which is documented here rather than
/// specially handled. Also carries the static expression builders <see cref="EventResourceDefinition.OnApplyFilter"/>
/// reuses for its own default-hide expression, so the two can't drift.
/// </remarks>
internal sealed class EventStatusFilterRewriter(
    ResourceFieldChainExpression statusChain, ResourceFieldChainExpression endsAtChain, DateTimeOffset now,
    bool canCompareEndsAt)
    : QueryExpressionRewriter<object?>
{
    /// <summary>Whether the walked expression mentioned <see cref="Event.Status"/> in any form.</summary>
    public bool ClientNamedStatus { get; private set; }

    /// <inheritdoc/>
    public override QueryExpression? VisitComparison(ComparisonExpression expression, object? argument)
    {
        if (expression.Operator == ComparisonOperator.Equals && expression.Left.Equals(statusChain)
            && expression.Right is LiteralConstantExpression { TypedValue: EventStatus status })
        {
            ClientNamedStatus = true;
            return Expand(status);
        }

        return base.VisitComparison(expression, argument);
    }

    /// <inheritdoc/>
    public override QueryExpression? VisitAny(AnyExpression expression, object? argument)
    {
        if (expression.MatchTarget.Equals(statusChain))
        {
            ClientNamedStatus = true;

            FilterExpression[] terms = expression.Constants
                .Select(constant => Expand((EventStatus)constant.TypedValue))
                .ToArray();

            return terms.Length == 1 ? terms[0] : new LogicalExpression(LogicalOperator.Or, terms);
        }

        return base.VisitAny(expression, argument);
    }

    /// <inheritdoc/>
    public override QueryExpression VisitResourceFieldChain(ResourceFieldChainExpression expression, object? argument)
    {
        if (expression.Equals(statusChain))
        {
            ClientNamedStatus = true;
        }

        return base.VisitResourceFieldChain(expression, argument);
    }

    private FilterExpression Expand(EventStatus status) => status switch
    {
        EventStatus.Live => LiveNotElapsed(statusChain, endsAtChain, now, canCompareEndsAt),
        EventStatus.Past => LiveElapsed(statusChain, endsAtChain, now, canCompareEndsAt),
        _ => StatusIs(statusChain, status)
    };

    internal static FilterExpression StatusIs(ResourceFieldChainExpression statusChain, EventStatus status) =>
        new ComparisonExpression(ComparisonOperator.Equals, statusChain, new LiteralConstantExpression(status));

    /// <remarks>
    /// <para>
    /// The <c>endsAt IS NULL</c> arm is load-bearing: an undated Live Event is never Past (CONTEXT.md's
    /// Starts at / Ends at entry), and <c>NOT(endsAt &lt;= now)</c> would silently drop it under SQL's
    /// three-valued <see langword="null"/> logic.
    /// </para>
    /// <para>
    /// <paramref name="canCompareEndsAt"/> is <see langword="false"/> under the SQLite test provider
    /// (ADR-0014) - EF Core's SQLite provider has never translated <c>&gt;</c>/<c>&lt;</c>/<c>&gt;=</c>/<c>&lt;=</c>
    /// on <see cref="DateTimeOffset"/>, only <c>==</c>/<c>!=</c> (a permanent provider limitation, not a bug -
    /// see <see href="https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations">the EF Core
    /// SQLite provider limitations doc</see>). SQL Server (production) has no such gap. When
    /// <see langword="false"/>, this degrades to <c>StatusIs(Live)</c> - every stored-Live row, elapsed or
    /// not - so the SQLite-backed Api test suite can't verify true elapsed-exclusion at the collection-filter
    /// level; that AC is verified end-to-end against the real engine instead, by
    /// <c>EventManagementScenarios</c> in <c>VirtualLeadersGuide.E2E.Tests</c> (real SQL Server via Aspire).
    /// See ADR-0053.
    /// </para>
    /// </remarks>
    internal static FilterExpression LiveNotElapsed(ResourceFieldChainExpression statusChain,
        ResourceFieldChainExpression endsAtChain, DateTimeOffset now, bool canCompareEndsAt)
    {
        FilterExpression statusIsLive = StatusIs(statusChain, EventStatus.Live);
        if (!canCompareEndsAt)
        {
            return statusIsLive;
        }

        return new LogicalExpression(LogicalOperator.And, statusIsLive,
            new LogicalExpression(LogicalOperator.Or,
                new ComparisonExpression(ComparisonOperator.Equals, endsAtChain, NullConstantExpression.Instance),
                new ComparisonExpression(ComparisonOperator.GreaterThan, endsAtChain, new LiteralConstantExpression(now))));
    }

    /// <remarks>
    /// See <see cref="LiveNotElapsed"/>'s remarks for <paramref name="canCompareEndsAt"/>. When
    /// <see langword="false"/> (SQLite), there is no SQLite-safe way to identify an elapsed row at the SQL
    /// level at all, so this degrades to a deterministic empty result rather than an approximation that could
    /// silently include the wrong rows.
    /// </remarks>
    internal static FilterExpression LiveElapsed(ResourceFieldChainExpression statusChain,
        ResourceFieldChainExpression endsAtChain, DateTimeOffset now, bool canCompareEndsAt)
    {
        if (!canCompareEndsAt)
        {
            return AlwaysFalse();
        }

        return new LogicalExpression(LogicalOperator.And,
            StatusIs(statusChain, EventStatus.Live),
            new ComparisonExpression(ComparisonOperator.LessOrEqual, endsAtChain, new LiteralConstantExpression(now)));
    }

    /// <summary>The default read-only collection view: <see cref="EventStatus.Draft"/> or not-yet-elapsed <see cref="EventStatus.Live"/>.</summary>
    internal static FilterExpression DefaultVisibleStatuses(ResourceFieldChainExpression statusChain,
        ResourceFieldChainExpression endsAtChain, DateTimeOffset now, bool canCompareEndsAt) =>
        new LogicalExpression(LogicalOperator.Or,
            StatusIs(statusChain, EventStatus.Draft), LiveNotElapsed(statusChain, endsAtChain, now, canCompareEndsAt));

    /// <remarks>
    /// Two bare literals, no field reference at all - comparing <see cref="Event.Status"/> (a non-nullable
    /// value type) to <see cref="NullConstantExpression"/> was considered instead and rejected: building that
    /// comparison risks an expression-tree construction error before translation is even attempted, since a
    /// non-nullable value-typed member can't validly compare against a null constant without an explicit
    /// nullable conversion this rewriter has no clean way to inject. Two constants avoid the question
    /// entirely - <c>0 = 1</c> is valid, provider-independent SQL on both engines.
    /// </remarks>
    private static FilterExpression AlwaysFalse() =>
        new ComparisonExpression(
            ComparisonOperator.Equals, new LiteralConstantExpression(0), new LiteralConstantExpression(1));
}
