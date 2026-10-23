using System.ComponentModel.DataAnnotations;
using MasidBaha.Application.Common.Enums;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Common.Options;

// Every tunable that used to be a magic number. Bound from configuration and
// validated at startup, so a bad value fails the boot instead of a request.

public class ReportOptions
{
    public const string Section = "Reports";

    // How long a report stays visible before it expires
    [Range(1, 72)] public int LifetimeHours { get; set; } = 6;

    [Range(1, 500)] public int MaxTopReports { get; set; } = 200;

    public TimeSpan Lifetime => TimeSpan.FromHours(LifetimeHours);
}

public class ExpiryOptions
{
    public const string Section = "Expiry";

    // How often the background sweeper flips stale reports to Expired
    [Range(1, 1440)] public int SweepIntervalMinutes { get; set; } = 15;
}

public class PushOptions
{
    public const string Section = "Push";

    // Reports at or above this severity trigger a push notification
    public Severity MinimumSeverity { get; set; } = Severity.KneeLevel;

    [Range(1, 10000)] public int QueueCapacity { get; set; } = 100;
}

// Web push keys. Optional: without them push is simply disabled.
public class VapidOptions
{
    public const string Section = "Vapid";

    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    public string? Subject { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey) &&
        !string.IsNullOrWhiteSpace(Subject);
}

public class PhotoOptions
{
    public const string Section = "Photos";

    // Anything bigger is rejected before we spend CPU decoding it
    [Range(1024, 50 * 1024 * 1024)] public long MaxUploadBytes { get; set; } = 8 * 1024 * 1024;

    [Range(200, 8000)] public int MaxLongestSidePixels { get; set; } = 1600;

    [Range(30, 100)] public int JpegQuality { get; set; } = 78;
}

public class RateLimitPolicyOptions
{
    [Range(1, 100000)] public int PermitLimit { get; set; }
    [Range(1, 3600)] public int WindowSeconds { get; set; } = 60;
}

public class RateLimitOptions
{
    public const string Section = "RateLimits";

    // Per client IP: there is no login, so the IP is the only handle we have
    [Required, ValidateObjectMembers] public RateLimitPolicyOptions ReportWrites { get; set; } = new() { PermitLimit = 5 };
    [Required, ValidateObjectMembers] public RateLimitPolicyOptions VoteWrites { get; set; } = new() { PermitLimit = 20 };
    [Required, ValidateObjectMembers] public RateLimitPolicyOptions PushWrites { get; set; } = new() { PermitLimit = 10 };
    [Required, ValidateObjectMembers] public RateLimitPolicyOptions AuthAttempts { get; set; } = new() { PermitLimit = 10 };
}

public class GeocodingOptions
{
    public const string Section = "Geocoding";

    [Required, Url] public string BaseAddress { get; set; } = "https://nominatim.openstreetmap.org/";
    [Required] public string UserAgent { get; set; } = "MasidBaha/1.0 (community flood reporting)";
    [Range(1, 60)] public int TimeoutSeconds { get; set; } = 5;

    // Nominatim's usage policy allows at most one request per second
    [Range(0, 60000)] public int MinIntervalMilliseconds { get; set; } = 1100;

    // Coordinates are rounded to this many decimals for the cache key (3 is roughly 110 m)
    [Range(1, 6)] public int CacheDecimals { get; set; } = 3;

    [Range(1, 10080)] public int CacheMinutes { get; set; } = 1440;
}

public class StorageOptions
{
    public const string Section = "Storage";

    // "Local" writes under wwwroot/uploads; "S3" uses any S3-compatible
    // service (Cloudflare R2, Supabase Storage, MinIO, AWS S3)
    public string Provider { get; set; } = "Local";

    public S3StorageOptions S3 { get; set; } = new();
}

public class S3StorageOptions
{
    public string? ServiceUrl { get; set; }
    public string? Bucket { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Region { get; set; } = "auto";

    // Public base URL that serves the bucket (e.g. the R2 public domain)
    public string? PublicBaseUrl { get; set; }
}

public class SecurityOptions
{
    public const string Section = "Security";

    // Turn on when running behind a reverse proxy / PaaS load balancer so the
    // real client IP and scheme are used (rate limiting, HTTPS redirect)
    public bool TrustForwardedHeaders { get; set; }

    // Redirect HTTP to HTTPS and send HSTS. Off by default because behind a
    // proxy that terminates TLS it needs TrustForwardedHeaders as well.
    public bool RequireHttps { get; set; }

    [Required] public string SessionCookieName { get; set; } = "mb_session";

    // Secret used to sign the anonymous session cookie (at least 32 characters).
    // Required outside Development; in Development a throwaway key is generated.
    [MinLength(32)] public string? SessionSigningKey { get; set; }

    // "Lax" works when the site and API share a registrable domain (and for
    // localhost); use "None" only if they are on different sites (requires HTTPS).
    [RegularExpression("^(Lax|Strict|None)$")] public string SessionCookieSameSite { get; set; } = "Lax";

    [Range(1, 3650)] public int SessionCookieDays { get; set; } = 365;
}

public class InitialAdminOptions
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string DisplayName { get; set; } = "Administrator";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

// Staff accounts (the back-office). Replaces the old shared X-Admin-Key.
public class AdminAuthOptions
{
    public const string Section = "AdminAuth";

    [Required] public string CookieName { get; set; } = "mb_admin";

    // How long a login lasts without activity (the cookie slides forward while in use)
    [Range(1, 168)] public int SessionHours { get; set; } = 12;

    // Length is what matters for password strength; no forced symbol rules
    [Range(8, 128)] public int MinPasswordLength { get; set; } = 12;

    // How often an existing login is re-checked against the account (role changes,
    // password changes and lockouts sign other sessions out within this window)
    [Range(0, 3600)] public int SecurityCheckSeconds { get; set; } = 60;

    [Range(1, 20)] public int MaxFailedAttempts { get; set; } = 5;
    [Range(1, 1440)] public int LockoutMinutes { get; set; } = 15;

    // Creates the first SuperAdmin on startup if no SuperAdmin exists yet.
    // Set via user-secrets / environment variables, never in a tracked file.
    public InitialAdminOptions InitialAdmin { get; set; } = new();
}
