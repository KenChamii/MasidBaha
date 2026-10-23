namespace MasidBaha.Application.Persistence;

public enum BanKind
{
    Session = 1, // one browser's anonymous session id
    Ip = 2       // a source address, stored only as a keyed hash
}

public enum BanMode
{
    Block = 1,  // submissions are refused
    Shadow = 2  // submissions look accepted but are never shown to anyone
}

// A staff decision to stop (or quietly ignore) one reporter source
public class ReporterBan
{
    public Guid Id { get; set; }
    public BanKind Kind { get; set; }

    // The session id, or the IP hash, depending on Kind
    public string Value { get; set; } = string.Empty;

    public BanMode Mode { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LiftedAt { get; set; }
    public string? LiftedByEmail { get; set; }

    public bool IsActive(DateTime now) => LiftedAt is null && (ExpiresAt is null || ExpiresAt > now);
}

// Who did what, and when. Written in the same save as the change it describes.
public class AuditEntry
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }

    // Small JSON document with the specifics (old/new values, counts, reasons)
    public string? Details { get; set; }
}

// A push announcement sent by staff
public class Broadcast
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Url { get; set; } = "/map";
    public DateTime SentAt { get; set; }
    public string SentByEmail { get; set; } = string.Empty;
    public int Recipients { get; set; }

    // Set when the announcement went only to people watching somewhere within this circle
    public double? AreaLat { get; set; }
    public double? AreaLng { get; set; }
    public int? AreaRadiusMeters { get; set; }
}

// A runtime override of a configured default (see RuntimeSettings)
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedByEmail { get; set; } = string.Empty;
}

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string PasswordChanged = "auth.password_changed";
    public const string TwoFactorEnabled = "auth.2fa_enabled";
    public const string TwoFactorDisabled = "auth.2fa_disabled";

    public const string ReportStatusChanged = "report.status_changed";
    public const string ReportDeleted = "report.deleted";
    public const string ReportsBulk = "report.bulk_action";
    public const string ReportsExported = "report.exported";

    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string UserReactivated = "user.reactivated";
    public const string UserPasswordReset = "user.password_reset";

    public const string BanCreated = "ban.created";
    public const string BanLifted = "ban.lifted";

    public const string BroadcastSent = "broadcast.sent";
    public const string SettingsChanged = "settings.changed";
    public const string SettingsReset = "settings.reset";
}
