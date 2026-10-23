using MasidBaha.Application.Admin;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Admin.Moderation;
using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Persistence;
using MasidBaha.Application.Trust;
using Microsoft.Extensions.Caching.Memory;

namespace MasidBaha.Tests;

// Builds the admin-side services by hand for tests that talk to the database directly
public static class TestServices
{
    public class FakeAdmin : ICurrentAdmin
    {
        public Guid? Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        public string Email { get; set; } = "tester@test.local";
    }

    // Fixed settings with the stock defaults, no database involved
    public class StaticSettings : IRuntimeSettings
    {
        public SettingsSnapshot Current { get; set; } = new()
        {
            ReportLifetimeHours = 6,
            PushMinimumSeverity = MasidBaha.Application.Common.Enums.Severity.KneeLevel,
            DenyFlagThreshold = 3,
            FloodingReportsPerHour = 5,
            ReportWritesPerMinute = 10,
            VoteWritesPerMinute = 30
        };

        public SettingsSnapshot Defaults => Current;
        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SettingsView> GetViewAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SettingsView { Current = Current, Defaults = Current });
        public Task<SettingsView> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SettingsView> ResetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public static IRuntimeSettings Settings() => new StaticSettings();

    public static IAuditLog Audit(MasidBahaDbContext db) => new AuditLog(db, new FakeAdmin());

    public static IBanService Bans(MasidBahaDbContext db) =>
        new BanService(db, Audit(db), new FakeAdmin(), new MemoryCache(new MemoryCacheOptions()));

    public static IModerationService Moderation(MasidBahaDbContext db) =>
        new ModerationService(db, Settings(), Audit(db));

    public static AdminReportsService AdminReports(MasidBahaDbContext db) =>
        new(db, Audit(db), Moderation(db), Bans(db), new SessionTrustService(db), Settings());
}
