using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.FloodReports.GetNearbyReports;

public interface IGetNearbyReportsService
{
    Task<List<FloodReportDto>> GetNearbyAsync(NearbyReportsQuery query);
}

public class GetNearbyReportsService : IGetNearbyReportsService
{
    private readonly MasidBahaDbContext _db;

    public GetNearbyReportsService(MasidBahaDbContext db)
    {
        _db = db;
    }

    public async Task<List<FloodReportDto>> GetNearbyAsync(NearbyReportsQuery query)
    {
        var center = FloodReportMappings.ToPoint(query.Lat, query.Lng);
        var now = DateTime.UtcNow;

        // IsWithinDistance becomes ST_DWithin on a geography column (metres),
        // which is what lets the GiST index do the radius filtering.
        // Expiry is checked here too, so correctness doesn't depend on the
        // background sweeper having run.
        var reports = await _db.FloodReports
            .AsNoTracking()
            .Where(r => r.Status == ReportStatus.Active
                        && r.ExpiresAt > now
                        && r.Location.IsWithinDistance(center, query.RadiusMeters))
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync();

        return reports.Select(r => r.ToDto()).ToList();
    }
}
