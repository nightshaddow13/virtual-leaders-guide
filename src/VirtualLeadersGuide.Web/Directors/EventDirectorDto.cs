namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// One row of an Event's Directors section (<c>EventEditor.razor</c>) - a Director joined against the one
/// Event-scoped <c>UserRole</c> Grant that put them there (P2-18, #113).
/// </summary>
/// <remarks>
/// A narrower sibling of <see cref="UserRowDto"/>, not a reuse of it: <see cref="UserRowDto"/> aggregates a
/// User across every Role/Grant they hold (<see cref="UserRowDto.EventGrantCount"/>,
/// <see cref="UserRowDto.RoleLabel"/>) for the Users screen, none of which this section reads, and it has no
/// field for the one thing this section needs to act - <see cref="GrantId"/>, the specific row a "Remove"
/// click deletes. Built by <see cref="ApiDirectorClient.GetDirectorsForEventAsync"/>.
/// </remarks>
public sealed class EventDirectorDto
{
    /// <summary>The Event-scoped <c>UserRole</c> row's own id - the target of <see cref="ApiDirectorClient.RemoveEventAccessAsync"/>.</summary>
    public required Guid GrantId { get; init; }

    public required string UserId { get; init; }

    public required string Email { get; init; }

    /// <remarks>Renders as the email when null, same convention as <see cref="UserRowDto.DisplayName"/>.</remarks>
    public string? DisplayName { get; init; }

    /// <summary>Whether a password has been set - <see langword="false"/> for a pending Invite.</summary>
    public required bool HasCredential { get; init; }

    /// <summary>
    /// Whether this Director separately holds the platform-wide Admin Role - ADR-0035 says Admin never
    /// has Grants, so a row with this set is invalid state (ADR-0051), not a normal Director. Drives
    /// <c>EventEditor.razor</c>'s disabled-remove-button guard.
    /// </summary>
    public required bool IsAdmin { get; init; }

    /// <summary>Display name where set, email otherwise - the label both the row and the removal confirm dialog use.</summary>
    public string DisplayLabel => DisplayName ?? Email;
}
