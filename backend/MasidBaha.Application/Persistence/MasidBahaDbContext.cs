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
            e.HasIndex(x => x.ReporterSessionId);
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
    }
}
