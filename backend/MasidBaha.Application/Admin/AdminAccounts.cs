using Microsoft.AspNetCore.Identity;

namespace MasidBaha.Application.Admin;

// A back-office account. Deliberately separate from the anonymous public
// session: only staff have one of these.
public class AdminUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";   // everything, including managing accounts
    public const string Moderator = "Moderator";     // review and act on reports
    public const string Viewer = "Viewer";           // read-only

    public static readonly string[] All = [SuperAdmin, Moderator, Viewer];
}

// Authorization policy names, so controllers never repeat role lists
public static class AdminPolicies
{
    public const string CanView = "admin:view";
    public const string CanModerate = "admin:moderate";
    public const string ManageUsers = "admin:manage-users";
}
