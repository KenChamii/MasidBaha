using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.FloodReports.GetHeatmapData;

public interface IGetHeatmapDataService
{
    Task<List<HeatmapPointDto>> GetHeatmapAsync(HeatmapQuery query);
}

public class GetHeatmapDataService : IGetHeatmapDataService
{
    private readonly MasidBahaDbContext _db;

    public GetHeatmapDataService(MasidBahaDbContext db)
    {
        _db = db;
    }

    public async Task<List<HeatmapPointDto>> GetHeatmapAsync(HeatmapQuery query)
    {
        // Historical view: intentionally does NOT filter by status, since the
        // whole point is to include Expired/Resolved reports too (that's the
        // "pattern over time" the heatmap is meant to reveal). Defaults to the
        // last 6 months if no range is given, to keep the payload bounded.
        var now = DateTime.UtcNow;
        var from = query.FromDate?.ToUtc() ?? now.AddMonths(-6);
        var to = query.ToDate?.ToUtc() ?? now;

        var reports = await _db.FloodReports
            .AsNoTracking()
            .Where(r => r.ReportedAt >= from && r.ReportedAt <= to)
            .Where(r => query.Region == null || r.Region == query.Region)
            .Where(r => query.Province == null || r.Province == query.Province)
            .Where(r => query.City == null || r.City == query.City)
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync();

        return reports.Select(r => new HeatmapPointDto
        {
            Lat = r.Location.Y,
            Lng = r.Location.X,
            Severity = r.Severity,
            Status = r.Status,
            ReportedAt = r.ReportedAt,
            Region = r.Region,
            Province = r.Province,
            City = r.City
        }).ToList();
    }
}
