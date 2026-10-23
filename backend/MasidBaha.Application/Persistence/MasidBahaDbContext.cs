using MasidBaha.Application.Admin;
using MasidBaha.Application.PushNotifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Persistence;

// Reports, votes and push subscriptions, plus the back-office accounts (ASP.NET Identity)
public class MasidBahaDbContext : IdentityDbContext<AdminUser, IdentityRole<Guid>, Guid>
{
    public const int Wgs84Srid = 4326;

    public MasidBahaDbContext(DbContextOptions<MasidBahaDbContext> options) : base(options) { }

    public DbSet<FloodReport> FloodReports => Set<FloodReport>();
    public DbSet<ReportVote> ReportVotes => Set<ReportVote>();
    public DbSet<StoredPushSubscription> PushSubscriptions => Set<StoredPushSubscription>();
    public DbSet<ReporterBan> ReporterBans => Set<ReporterBan>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();
    public DbSet<WatchArea> WatchAreas => Set<WatchArea>();
    public DbSet<CachedDocument> CachedDocuments => Set<CachedDocument>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity first: it configures its own tables, which we then rename below
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<AdminUser>(e =>
        {
            e.ToTable("admin_users");
            e.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Ignore(x => x.IsActive);
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("admin_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("admin_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("admin_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("admin_user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("admin_user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("admin_role_claims");

        modelBuilder.Entity<FloodReport>(e =>
        {
            e.ToTable("flood_reports", t =>
            {
                t.HasCheckConstraint("ck_flood_reports_severity", "severity BETWEEN 1 AND 4");
                t.HasCheckConstraint("ck_flood_reports_status", "status BETWEEN 1 AND 3");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

            // geography (not geometry) so distances are in metres on the spheroid
            e.Property(x => x.Location).HasColumnType("geography (point, 4326)");

            // enums are stored as smallint, which also keeps signed values safe
            e.Property(x => x.Severity).HasConversion<short>();
            e.Property(x => x.Status).HasConversion<short>();

            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.ReporterSessionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Region).HasMaxLength(100);
            e.Property(x => x.Province).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.ReportedAt).HasDefaultValueSql("now()");

            // GiST makes ST_DWithin radius queries use the index
            e.HasIndex(x => x.Location).HasMethod("gist");
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasIndex(x => new { x.Region, x.Province, x.City });
            e.HasIndex(x => x.ReportedAt);

            // One report per request id. The id is a random UUID made by the app, so it is safe to key on it alone: a
            // visitor whose first answer was lost never received a session cookie, and the retry has to match anyway.
            // (Reports made without an id are not limited.)
            e.HasIndex(x => x.ClientRequestId)
                .IsUnique()
                .HasFilter("client_request_id IS NOT NULL")
                .HasDatabaseName("ux_flood_reports_client_request");
            e.HasIndex(x => x.ReporterSessionId);
            e.Property(x => x.ReporterIpHash).HasMaxLength(64);
            e.HasIndex(x => x.ReporterIpHash);
        });

        modelBuilder.Entity<ReportVote>(e =>
        {
            e.ToTable("report_votes", t =>
                t.HasCheckConstraint("ck_report_votes_vote_type", "vote_type IN (-1, 1)"));

            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.VoterSessionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.VoteType).HasConversion<short>();
            e.Property(x => x.VotedAt).HasDefaultValueSql("now()");

            e.HasOne(x => x.FloodReport)
                .WithMany(x => x.Votes)
                .HasForeignKey(x => x.FloodReportId)
                .OnDelete(DeleteBehavior.Cascade);

            // one vote per session per report
            e.HasIndex(x => new { x.FloodReportId, x.VoterSessionId }).IsUnique();
        });

        // One row per browser subscription, keyed on endpoint: a device can
        // re-subscribe after clearing storage, and the endpoint is what
        // identifies the push channel to the browser.
        modelBuilder.Entity<StoredPushSubscription>(e =>
        {
            e.ToTable("push_subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SessionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
            e.Property(x => x.P256dh).HasMaxLength(500).IsRequired();
            e.Property(x => x.Auth).HasMaxLength(500).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.Endpoint).IsUnique();
        });

        modelBuilder.Entity<WatchArea>(e =>
        {
            e.ToTable("watch_areas");
            e.HasKey(x => x.Id);
            e.Property(x => x.SessionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.Property(x => x.Location).HasColumnType("geography (point, 4326)");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.SessionId);
            // GiST so "which areas are near this report?" uses the index
            e.HasIndex(x => x.Location).HasMethod("gist");
        });

        modelBuilder.Entity<ReporterBan>(e =>
        {
            e.ToTable("reporter_bans", t =>
            {
                t.HasCheckConstraint("ck_reporter_bans_kind", "kind IN (1, 2)");
                t.HasCheckConstraint("ck_reporter_bans_mode", "mode IN (1, 2)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Kind).HasConversion<short>();
            e.Property(x => x.Mode).HasConversion<short>();
            e.Property(x => x.Value).HasMaxLength(128).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.Property(x => x.CreatedByEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.LiftedByEmail).HasMaxLength(256);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.Kind, x.Value });
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("admin_audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.At).HasDefaultValueSql("now()");
            e.Property(x => x.ActorEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(32).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.TargetType, x.TargetId });
            e.HasIndex(x => x.Action);
        });

        modelBuilder.Entity<Broadcast>(e =>
        {
            e.ToTable("broadcasts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Title).HasMaxLength(80).IsRequired();
            e.Property(x => x.Body).HasMaxLength(300).IsRequired();
            e.Property(x => x.Url).HasMaxLength(200).IsRequired();
            e.Property(x => x.SentByEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.SentAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.SentAt);
        });

        modelBuilder.Entity<CachedDocument>(e =>
        {
            e.ToTable("cached_documents");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("app_settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Value).HasMaxLength(200).IsRequired();
            e.Property(x => x.UpdatedByEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
    }
}
