namespace VirtualLeadersGuide.Web.Directors;

/// <summary>Outcomes <see cref="DirectorInviteService.DeleteAsync"/> distinguishes.</summary>
public enum UserDeleteOutcome
{
    /// <remarks>Full teardown (ADR-0045) - the <c>ApplicationUser</c> row is deleted, and the database's own cascade removes its <c>UserRole</c> rows: the Director Role, and every Event-scoped Grant.</remarks>
    Deleted,

    NotFound,

    /// <remarks>ADR-0045's Admin-target guard - the row would silently reappear as Admin on next sign-in (ADR-0008), so this action refuses it.</remarks>
    TargetIsAdmin,

    /// <remarks>ADR-0045's self-target guard - self-service deletion is <c>Manage/DeletePersonalData.razor</c>'s own flow, not this one.</remarks>
    TargetIsSelf,

    /// <remarks>The Director store (Api) didn't answer while checking whether the target holds Admin - see <see cref="DirectorInviteService.DeleteAsync"/>'s remarks.</remarks>
    StoreUnavailable
}
