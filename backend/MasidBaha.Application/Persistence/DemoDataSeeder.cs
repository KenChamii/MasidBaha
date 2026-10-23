using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Persistence;

// Fills the map with realistic reports so a fresh install or demo never looks
// empty. Re-running replaces the previous demo rows (and only those), which
// also refreshes their expiry so the "active" ones stay visible.
public static class DemoDataSeeder
{
    public const string DemoSessionId = "demo-seed";

    private record Spot(double Lat, double Lng, string Region, string Province, string City, string Street);

    private static readonly Spot[] Spots =
    {
        new(14.6507, 121.1029, "National Capital Region", "Metro Manila", "Marikina", "Marcos Highway"),
        new(14.6350, 121.0990, "National Capital Region", "Metro Manila", "Marikina", "Sumulong Highway"),
        new(14.6760, 121.0437, "National Capital Region", "Metro Manila", "Quezon City", "Commonwealth Avenue"),
        new(14.5547, 121.0244, "National Capital Region", "Metro Manila", "Makati", "EDSA Guadalupe"),
        new(14.5995, 120.9842, "National Capital Region", "Metro Manila", "Manila", "Espana Boulevard"),
        new(14.5794, 121.1070, "Calabarzon", "Rizal", "Cainta", "Ortigas Avenue Extension"),
        new(14.2767, 121.1234, "Calabarzon", "Laguna", "Cabuyao", "National Highway"),
        new(14.1648, 121.2413, "Calabarzon", "Laguna", "Los Banos", "Lopez Avenue"),
        new(14.2117, 121.1642, "Calabarzon", "Laguna", "Santa Rosa", "Pulong Santa Cruz"),
        new(10.3157, 123.8854, "Central Visayas", "Cebu", "Cebu City", "Colon Street"),
        new(10.3313, 123.9490, "Central Visayas", "Cebu", "Mandaue", "A. S. Fortuna Street"),
        new(10.7202, 122.5621, "Western Visayas", "Iloilo", "Iloilo City", "Diversion Road"),
        new(7.0731, 125.6128, "Davao Region", "Davao del Sur", "Davao City", "Bankerohan"),
        new(7.1907, 125.4553, "Davao Region", "Davao del Sur", "Davao City", "Panacan")
    };

    private static readonly string[] Notes =
    {
        "Water is rising quickly near the intersection.",
        "Cars are turning back, motorcycles cannot pass.",
        "Ankle-deep and slowly draining.",
        "Drainage is clogged, water has been here for an hour.",
        null!, null!
    };

    public static async Task SeedAsync(MasidBahaDbContext db, CancellationToken cancellationToken = default)
    {
        await db.FloodReports
            .Where(r => r.ReporterSessionId == DemoSessionId)
            .ExecuteDeleteAsync(cancellationToken);

        var random = new Random(42); // fixed seed: the demo looks the same every time
        var now = DateTime.UtcNow;
        var reports = new List<FloodReport>();

        // Fresh reports that are visible on the live map right now
        for (var i = 0; i < Spots.Length; i++)
        {
            var spot = Spots[i];
            var reportedAt = now - TimeSpan.FromMinutes(random.Next(5, 300));
            reports.Add(Build(spot, reportedAt, reportedAt + ReportDefaults.Lifetime,
                (Severity)random.Next(1, 5), ReportStatus.Active, random.Next(1, 40), Notes[i % Notes.Length]));
        }

        // Older, closed reports so the analytics heatmap shows recurring hotspots
        for (var i = 0; i < 120; i++)
        {
            var spot = Spots[random.Next(Spots.Length)];
            var reportedAt = now - TimeSpan.FromDays(random.Next(1, 170)) - TimeSpan.FromMinutes(random.Next(0, 1440));
            reports.Add(Build(spot, reportedAt, reportedAt + ReportDefaults.Lifetime,
                (Severity)random.Next(1, 5), random.Next(2) == 0 ? ReportStatus.Resolved : ReportStatus.Expired,
                random.Next(1, 30), null));
        }

        db.FloodReports.AddRange(reports);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static FloodReport Build(Spot spot, DateTime reportedAt, DateTime expiresAt, Severity severity,
        ReportStatus status, int confidence, string? notes)
    {
        // Jitter so repeated hotspots spread over a few streets instead of one pixel
        var jitter = () => (Random.Shared.NextDouble() - 0.5) * 0.004;

        return new FloodReport
        {
            Id = Guid.CreateVersion7(),
            Location = FloodReportMappings.ToPoint(spot.Lat + jitter(), spot.Lng + jitter()),
            Severity = severity,
            Notes = notes is null ? null : $"{spot.Street}: {notes}",
            ReportedAt = reportedAt,
            ExpiresAt = expiresAt,
            ConfidenceScore = confidence,
            Status = status,
            ReporterSessionId = DemoSessionId,
            Region = spot.Region,
            Province = spot.Province,
            City = spot.City
        };
    }
}
