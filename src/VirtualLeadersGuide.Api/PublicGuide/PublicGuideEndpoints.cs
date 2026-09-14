using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.PublicGuide;

/// <summary>
/// The anonymous-reachable surface behind the public Leaders Guide gate (P4-2, #72) - an Event lookup by
/// Slug, and a Passcode check, for a visitor with no signed-in identity at all.
/// </summary>
/// <remarks>
/// Gated by the same <c>X-Internal-Key</c> fallback policy as every other Api endpoint (ADR-0015), never
/// <c>RequireInternalUser</c> - unlike <c>/api/*</c>, which needs a signed-in user's internal JWT, an
/// anonymous visitor's Web request has none to send. Deliberately outside JsonApi's <c>/api</c> namespace,
/// alongside <see cref="Authorization.InternalAuthorizationEndpoints"/> and
/// <see cref="Identity.InternalIdentityEndpoints"/> - the third member of that "plain REST, X-Internal-Key
/// only" family, not a JSON:API resource. Never returns <c>Event.Passcode</c> in any form, plaintext or
/// ciphertext - only whether a submitted guess matched (<see cref="CheckPasscodeAsync"/>).
/// </remarks>
public static class PublicGuideEndpoints
{
    public static IEndpointRouteBuilder MapPublicGuideEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup(PublicGuideRoutes.GroupPrefix);

        group.MapGet(PublicGuideRoutes.EventBySlug, GetEventBySlugAsync);
        group.MapPost(PublicGuideRoutes.PasscodeCheck, CheckPasscodeAsync);

        return app;
    }

    /// <remarks>
    /// <c>404</c> for an unknown Slug and for a <c>Draft</c> Event alike (P4-2, #72) - a <c>Draft</c> Event
    /// has no public existence yet, and this endpoint can't distinguish "no such address" from "not
    /// published yet" without confirming an unpublished Event's Slug is real to whoever's asking. This
    /// applies to every caller, staff included - Web's own signed-in staff bypass reaches this endpoint the
    /// same as an anonymous visitor and gets the same <c>404</c> for a Draft Event; see the linked plan for
    /// why that gap is accepted rather than closed here.
    /// </remarks>
    private static async Task<IResult> GetEventBySlugAsync(
        string slug, VirtualLeadersGuideDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        Event? @event = await db.Events.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Slug == slug.ToLowerInvariant(), cancellationToken);

        if (@event is null || @event.Status == EventStatus.Draft)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToDto(@event, timeProvider.GetUtcNow()));
    }

    /// <remarks>
    /// Always <c>200</c> with <see cref="PasscodeCheckResult.Matched"/> set for a real Event, never a
    /// <c>401</c>/<c>403</c> for a wrong guess - see <see cref="PasscodeCheckResult"/>'s remarks.
    /// <see cref="Event.Passcode"/> is already plaintext by the time it reaches here -
    /// <see cref="DataProtectionStringConverter"/> decrypts it at materialization - so the comparison below
    /// is a plain in-memory string compare, never pushed into the query (that converter's own remarks explain
    /// why it can't be: <c>Protect</c> is non-deterministic, so <c>Where(e =&gt; e.Passcode == guess)</c>
    /// could never match even the right plaintext).
    /// </remarks>
    private static async Task<IResult> CheckPasscodeAsync(
        string slug, PasscodeCheckRequest request, VirtualLeadersGuideDbContext db,
        CancellationToken cancellationToken)
    {
        Event? @event = await db.Events.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Slug == slug.ToLowerInvariant(), cancellationToken);

        if (@event is null || @event.Status == EventStatus.Draft)
        {
            return Results.NotFound();
        }

        bool matched = string.Equals(
            NormalizePasscode(request.Passcode), NormalizePasscode(@event.Passcode!), StringComparison.OrdinalIgnoreCase);

        return Results.Ok(new PasscodeCheckResult
        {
            Matched = matched,
            EventId = matched ? @event.Id : null,
            PasscodeVersion = matched ? @event.PasscodeVersion : null
        });
    }

    /// <remarks>
    /// Strips every whitespace character, not just leading/trailing (ADR-0027; <c>PasscodeGenerator</c>'s own
    /// contract is "two words" concatenated with no separator, e.g. <c>TigerLantern</c>, but a visitor
    /// reading that off a printed handout has every reason to type <c>Tiger Lantern</c> - treating the space
    /// as insignificant matches what the Passcode conceptually is, not just how it happens to be stored).
    /// Case is folded separately, at the comparison site (<see cref="StringComparison.OrdinalIgnoreCase"/>),
    /// not here. Applied to both sides of every comparison, so a stored value's own shape never needs
    /// special-casing separately from a visitor's guess.
    /// </remarks>
    private static string NormalizePasscode(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static PublicEventDto ToDto(Event @event, DateTimeOffset now) => new()
    {
        Id = @event.Id,
        Name = @event.Name,
        Slug = @event.Slug!,
        Status = EventStatusRules.EffectiveStatus(@event.Status, @event.EndsAt, now).ToString(),
        StartsAt = @event.StartsAt,
        EndsAt = @event.EndsAt,
        PasscodeVersion = @event.PasscodeVersion
    };
}
