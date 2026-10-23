using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MasidBaha.Tests.Integration;

// One real PostGIS container shared by every integration test class. Spatial
// queries, row locks and unique constraints only behave like production when
// they run against the real database, so these tests don't use an in-memory fake.
public class PostgisFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:17-3.5")
        .WithDatabase("masidbaha_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public string ConnectionString => _container.GetConnectionString();

    public MasidBahaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MasidBahaDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MasidBahaDbContext(options);
    }

    // Each test starts from empty tables
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE report_votes, flood_reports, push_subscriptions, watch_areas, reporter_bans, admin_audit_log, broadcasts, app_settings RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition(Name)]
public class PostgisCollection : ICollectionFixture<PostgisFixture>
{
    public const string Name = "postgis";
}
