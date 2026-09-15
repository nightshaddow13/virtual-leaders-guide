namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Resource-specific JSON:API envelope shapes <see cref="ApiDirectorClient"/> sends to and reads from
/// <c>/api/users</c> and <c>/api/roleGrants</c> - <see langword="internal"/> wire-format detail, not
/// exposed past this client; callers see <see cref="UserRowDto"/> and the outcome enums instead. The
/// resource-shaped-nothing envelope plumbing (<c>ErrorDocument</c>/<c>ErrorObject</c>/<c>ErrorSource</c>/
/// <c>DocumentMeta</c>) lives in <see cref="VirtualLeadersGuide.Web.JsonApi"/> instead, shared with
/// <c>Events</c>/<c>InfoPages</c> - this client doesn't currently read error pointers, so it has no need to
/// import that namespace.
/// </summary>
internal sealed class UserResourceObject
{
    public required string Type { get; init; }

    public string? Id { get; init; }

    public UserAttributesDto? Attributes { get; init; }
}

/// <summary>A User's <c>email</c>/<c>displayName</c>/<c>hasCredential</c> attributes, as read from <c>/api/users</c>.</summary>
internal sealed class UserAttributesDto
{
    public string? Email { get; init; }

    public string? DisplayName { get; init; }

    public bool? HasCredential { get; init; }
}

/// <summary>A single-resource JSON:API document - the request/response body for GET-single on <c>/api/users</c>.</summary>
internal sealed class UserDocument
{
    public required UserResourceObject Data { get; init; }
}

/// <summary>The response body for <c>GET /api/users</c>.</summary>
internal sealed class UserCollectionDocument
{
    public required List<UserResourceObject> Data { get; init; }
}

/// <summary>A <c>UserRole</c> resource - either a Role (unscoped) or a Grant (Event-scoped), per ADR-0035.</summary>
internal sealed class RoleGrantResourceObject
{
    public required string Type { get; init; }

    public string? Id { get; init; }

    public RoleGrantAttributesDto? Attributes { get; init; }
}

/// <summary>A <c>UserRole</c> row's <c>userId</c>/<c>roleId</c>/<c>eventId</c> attributes, as sent or received.</summary>
internal sealed class RoleGrantAttributesDto
{
    public string? UserId { get; init; }

    public int? RoleId { get; init; }

    public Guid? EventId { get; init; }
}

/// <summary>A single-resource JSON:API document - the request body for POST and the response body for GET-single/POST on <c>/api/roleGrants</c>.</summary>
internal sealed class RoleGrantDocument
{
    public required RoleGrantResourceObject Data { get; init; }
}

/// <summary>The response body for <c>GET /api/roleGrants</c>.</summary>
internal sealed class RoleGrantCollectionDocument
{
    public required List<RoleGrantResourceObject> Data { get; init; }
}
