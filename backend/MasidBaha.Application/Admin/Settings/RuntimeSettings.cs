using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Admin.Settings;

// The values staff may change while the app runs. Everything else stays in configuration.
public class SettingsSnapshot
{
    public int ReportLifetimeHours { get; set; }
    public Severity PushMinimumSeverity { get; set; }
    public int DenyFlagThreshold { get; set; }
    public int FloodingReportsPerHour { get; set; }
    public int ReportWritesPerMinute { get; set; }
    public int VoteWritesPerMinute { get; set; }

    // Bumped on every change, so rate-limit partitions pick up new limits at once
    public long Version { get; set; }

    public TimeSpan ReportLifetime => TimeSpan.FromHours(ReportLifetimeHours);
}

public class UpdateSettingsRequest
{
    public int ReportLifetimeHours { get; set; }
    public Severity PushMinimumSeverity { get; set; }
    public int DenyFlagThreshold { get; set; }
    public int FloodingReportsPerHour { get; set; }
    public int ReportWritesPerMinute { get; set; }
    public int VoteWritesPerMinute { get; set; }
}

public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.ReportLifetimeHours).InclusiveBetween(1, 72);
        RuleFor(x => x.PushMinimumSeverity).IsInEnum();
        RuleFor(x => x.DenyFlagThreshold).InclusiveBetween(1, 20);
        RuleFor(x => x.FloodingReportsPerHour).InclusiveBetween(2, 100);
        RuleFor(x => x.ReportWritesPerMinute).InclusiveBetween(1, 1000);
        RuleFor(x => x.VoteWritesPerMinute).InclusiveBetween(1, 1000);
    }
}

// What the settings page shows: the effective value and what it would be without an override
public class SettingsView
{
    public SettingsSnapshot Current { get; set; } = new();
    public SettingsSnapshot Defaults { get; set; } = new();
    public List<string> Overridden { get; set; } = new();
}

public interface IRuntimeSettings
{
    // Always available and cheap: a snapshot held in memory
    SettingsSnapshot Current { get; }

    SettingsSnapshot Defaults { get; }

    Task ReloadAsync(CancellationToken cancellationToken = default);
    Task<SettingsView> GetViewAsync(CancellationToken cancellationToken = default);
    Task<SettingsView> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default);
    Task<SettingsView> ResetAsync(CancellationToken cancellationToken = default);
}

// Database overrides on top of the configured defaults. Held in memory and
// reloaded on every change and once a minute, so several API instances agree.
public class RuntimeSettings : IRuntimeSettings
{
    public const string LifetimeKey = "reports.lifetimeHours";
    public const string PushSeverityKey = "push.minimumSeverity";
    public const string DenyKey = "moderation.denyFlagThreshold";
    public const string FloodingKey = "moderation.floodingReportsPerHour";
    public const string ReportWritesKey = "rateLimits.reportWritesPerMinute";
    public const string VoteWritesKey = "rateLimits.voteWritesPerMinute";

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RuntimeSettings> _logger;
    private volatile SettingsSnapshot _current;
    private long _version;

    public SettingsSnapshot Defaults { get; }

    public RuntimeSettings(
        IServiceScopeFactory scopes,
        IOptions<ReportOptions> reports,
        IOptions<PushOptions> push,
        IOptions<RateLimitOptions> limits,
        ILogger<RuntimeSettings> logger)
    {
        _scopes = scopes;
        _logger = logger;

        Defaults = new SettingsSnapshot
        {
            ReportLifetimeHours = reports.Value.LifetimeHours,
            PushMinimumSeverity = push.Value.MinimumSeverity,
            DenyFlagThreshold = 2,
            FloodingReportsPerHour = 5,
            ReportWritesPerMinute = limits.Value.ReportWrites.PermitLimit,
            VoteWritesPerMinute = limits.Value.VoteWrites.PermitLimit
        };
        _current = Clone(Defaults, 0);
    }

    public SettingsSnapshot Current => _current;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasidBahaDbContext>();
        var overrides = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var loaded = Apply(overrides, _current.Version);

        // The version only moves when a value does: rate-limit partitions are keyed on it,
        // so bumping it on every refresh would reset everyone's counters once a minute
        if (!SameValues(loaded, _current))
        {
            loaded.Version = Interlocked.Increment(ref _version);
            _current = loaded;
        }
    }

    public async Task<SettingsView> GetViewAsync(CancellationToken cancellationToken = default)
    {
        await ReloadAsync(cancellationToken);

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasidBahaDbContext>();
        var keys = await db.AppSettings.AsNoTracking().Select(s => s.Key).ToListAsync(cancellationToken);

        return new SettingsView { Current = _current, Defaults = Defaults, Overridden = keys.Order().ToList() };
    }

    public async Task<SettingsView> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MasidBahaDbContext>();
            var admin = scope.ServiceProvider.GetRequiredService<ICurrentAdmin>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var email = string.IsNullOrEmpty(admin.Email) ? "system" : admin.Email;

            var existing = await db.AppSettings.ToDictionaryAsync(s => s.Key, cancellationToken);
            var before = _current;

            void Set(string key, string value)
            {
                if (existing.TryGetValue(key, out var row))
                {
                    row.Value = value;
                    row.UpdatedAt = DateTime.UtcNow;
                    row.UpdatedByEmail = email;
                }
                else
                {
                    db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow, UpdatedByEmail = email });
                }
            }

            Set(LifetimeKey, request.ReportLifetimeHours.ToString());
            Set(PushSeverityKey, ((int)request.PushMinimumSeverity).ToString());
            Set(DenyKey, request.DenyFlagThreshold.ToString());
            Set(FloodingKey, request.FloodingReportsPerHour.ToString());
            Set(ReportWritesKey, request.ReportWritesPerMinute.ToString());
            Set(VoteWritesKey, request.VoteWritesPerMinute.ToString());

            audit.Add(AuditActions.SettingsChanged, "settings", details: new
            {
                before = Summarise(before),
                after = request
            });

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetViewAsync(cancellationToken);
    }

    public async Task<SettingsView> ResetAsync(CancellationToken cancellationToken = default)
    {
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MasidBahaDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

            await db.AppSettings.ExecuteDeleteAsync(cancellationToken);
            audit.Add(AuditActions.SettingsReset, "settings");
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetViewAsync(cancellationToken);
    }

    private SettingsSnapshot Apply(IReadOnlyDictionary<string, string> overrides, long version)
    {
        int Int(string key, int fallback, int min, int max) =>
            overrides.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) && value >= min && value <= max
                ? value
                : fallback;

        var snapshot = Clone(Defaults, version);
        snapshot.ReportLifetimeHours = Int(LifetimeKey, Defaults.ReportLifetimeHours, 1, 72);
        snapshot.PushMinimumSeverity = (Severity)Int(PushSeverityKey, (int)Defaults.PushMinimumSeverity, 1, 4);
        snapshot.DenyFlagThreshold = Int(DenyKey, Defaults.DenyFlagThreshold, 1, 20);
        snapshot.FloodingReportsPerHour = Int(FloodingKey, Defaults.FloodingReportsPerHour, 2, 100);
        snapshot.ReportWritesPerMinute = Int(ReportWritesKey, Defaults.ReportWritesPerMinute, 1, 1000);
        snapshot.VoteWritesPerMinute = Int(VoteWritesKey, Defaults.VoteWritesPerMinute, 1, 1000);
        return snapshot;
    }

    private static bool SameValues(SettingsSnapshot a, SettingsSnapshot b) =>
        a.ReportLifetimeHours == b.ReportLifetimeHours
        && a.PushMinimumSeverity == b.PushMinimumSeverity
        && a.DenyFlagThreshold == b.DenyFlagThreshold
        && a.FloodingReportsPerHour == b.FloodingReportsPerHour
        && a.ReportWritesPerMinute == b.ReportWritesPerMinute
        && a.VoteWritesPerMinute == b.VoteWritesPerMinute;

    private static SettingsSnapshot Clone(SettingsSnapshot s, long version) => new()
    {
        ReportLifetimeHours = s.ReportLifetimeHours,
        PushMinimumSeverity = s.PushMinimumSeverity,
        DenyFlagThreshold = s.DenyFlagThreshold,
        FloodingReportsPerHour = s.FloodingReportsPerHour,
        ReportWritesPerMinute = s.ReportWritesPerMinute,
        VoteWritesPerMinute = s.VoteWritesPerMinute,
        Version = version
    };

    private static object Summarise(SettingsSnapshot s) => new
    {
        s.ReportLifetimeHours,
        s.PushMinimumSeverity,
        s.DenyFlagThreshold,
        s.FloodingReportsPerHour,
        s.ReportWritesPerMinute,
        s.VoteWritesPerMinute
    };
}
