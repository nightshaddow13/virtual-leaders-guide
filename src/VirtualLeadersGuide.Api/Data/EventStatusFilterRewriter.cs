using JsonApiDotNetCore.Queries.Expressions;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// The four pieces of context every effective-status expression in this file needs, bundled instead of
/// travelling as four separate parameters through <see cref="EventStatusFilterRewriter"/>'s constructor and
/// every static builder below.
/// </summary>
/// <param name="StatusChain">The <see cref="Event.Status"/> field, as a query expression.</param>
/// <param name="EndsAtChain">The <see cref="Event.EndsAt"/> field, as a query expression.</param>
/// <param name="Now">
/// The request's single "now" - resolved once by the caller so every branch of one query agrees on the
/// boundary, rather than each comparison reading the clock separately.
/// </param>
/// <param name="CanCompareEndsAt">
/// <see langword="false"/> under the SQLite test provider (ADR-0014) - EF Core's SQLite provider has never
/// translated <c>&gt;</c>/<c>&lt;</c>/<c>&gt;=</c>/<c>&lt;=</c> on <see cref="DateTimeOffset"/>, only
/// <c>==</c>/<c>!=</c> (a permanent provider limitation, not a bug - see
/// <see href="https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations">the EF Core SQLite
/// provider limitations doc</see>). SQL Server (production) has no such gap. See ADR-0053.
/// </param>
internal readonly record struct EventStatusFilterContext(
    ResourceFieldChainExpression StatusChain, ResourceFieldChainExpression EndsAtChain, DateTimeOffset Now,
    bool CanCompareEndsAt);

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
internal sealed class EventStatusFilterRewriter(EventStatusFilterContext context)
    : QueryExpressionRewriter<object?>
{
    /// <summary>Whether the walked expression mentioned <see cref="Event.Status"/> in any form.</summary>
    public bool ClientNamedStatus { get; private set; }

    /// <inheritdoc/>
    public override QueryExpression? VisitComparison(ComparisonExpression expression, object? argument)
    {
        if (expression.Operator == ComparisonOperator.Equals && expression.Left.Equals(context.StatusChain)
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
        if (expression.MatchTarget.Equals(context.StatusChain))
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
        if (expression.Equals(context.StatusChain))
        {
            ClientNamedStatus = true;
        }

        return base.VisitResourceFieldChain(expression, argument);
    }

    private FilterExpression Expand(EventStatus status) => status switch
    {
        EventStatus.Live => LiveNotElapsed(context),
        EventStatus.Past => LiveElapsed(context),
        _ => StatusIs(context, status)
    };

    internal static FilterExpression StatusIs(EventStatusFilterContext context, EventStatus status) =>
        new ComparisonExpression(ComparisonOperator.Equals, context.StatusChain, new LiteralConstantExpression(status));

    /// <remarks>
    /// The <c>endsAt IS NULL</c> arm is load-bearing: an undated Live Event is never Past (CONTEXT.md's
    /// Starts at / Ends at entry), and <c>NOT(endsAt &lt;= now)</c> would silently drop it under SQL's
    /// three-valued <see langword="null"/> logic. When <see cref="EventStatusFilterContext.CanCompareEndsAt"/>
    /// is <see langword="false"/> (SQLite), this degrades to <c>StatusIs(Live)</c> - every stored-Live row,
    /// elapsed or not - so the SQLite-backed Api test suite can't verify true elapsed-exclusion at the
    /// collection-filter level; that AC is verified end-to-end against the real engine instead, by
    /// <c>EventManagementScenarios</c> in <c>VirtualLeadersGuide.E2E.Tests</c> (real SQL Server via Aspire).
    /// </remarks>
    internal static FilterExpression LiveNotElapsed(EventStatusFilterContext context)
    {
        FilterExpression statusIsLive = StatusIs(context, EventStatus.Live);
        if (!context.CanCompareEndsAt)
        {
            return statusIsLive;
        }

        return new LogicalExpression(LogicalOperator.And, statusIsLive,
            new LogicalExpression(LogicalOperator.Or,
                new ComparisonExpression(ComparisonOperator.Equals, context.EndsAtChain, NullConstantExpression.Instance),
                new ComparisonExpression(ComparisonOperator.GreaterThan, context.EndsAtChain, new LiteralConstantExpression(context.Now))));
    }

    /// <remarks>
    /// See <see cref="LiveNotElapsed"/>'s remarks for <see cref="EventStatusFilterContext.CanCompareEndsAt"/>.
    /// When <see langword="false"/> (SQLite), there is no SQLite-safe way to identify an elapsed row at the
    /// SQL level at all, so this degrades to a deterministic empty result rather than an approximation that
    /// could silently include the wrong rows.
    /// </remarks>
    internal static FilterExpression LiveElapsed(EventStatusFilterContext context)
    {
        if (!context.CanCompareEndsAt)
        {
            return AlwaysFalse();
        }

        return new LogicalExpression(LogicalOperator.And,
            StatusIs(context, EventStatus.Live),
            new ComparisonExpression(ComparisonOperator.LessOrEqual, context.EndsAtChain, new LiteralConstantExpression(context.Now)));
    }

    /// <summary>The default read-only collection view: <see cref="EventStatus.Draft"/> or not-yet-elapsed <see cref="EventStatus.Live"/>.</summary>
    internal static FilterExpression DefaultVisibleStatuses(EventStatusFilterContext context) =>
        new LogicalExpression(LogicalOperator.Or, StatusIs(context, EventStatus.Draft), LiveNotElapsed(context));

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
