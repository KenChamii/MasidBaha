using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.FloodReports.GetTopReports;

public interface IGetTopReportsService
{
    Task<List<FloodReportDto>> GetTopAsync(TopReportsQuery query);
}

public class GetTopReportsService : IGetTopReportsService
{
    private readonly MasidBahaDbContext _db;

    public GetTopReportsService(MasidBahaDbContext db)
    {
        _db = db;
    }

    public async Task<List<FloodReportDto>> GetTopAsync(TopReportsQuery query)
    {
        var now = DateTime.UtcNow;
        var limit = Math.Clamp(query.Limit, 1, ReportDefaults.MaxTopReports);

        var reports = await _db.FloodReports
            .AsNoTracking()
            .Where(r => r.Status == ReportStatus.Active && r.ExpiresAt > now)
            .Where(r => query.Region == null || r.Region == query.Region)
            .Where(r => query.Province == null || r.Province == query.Province)
            .Where(r => query.City == null || r.City == query.City)
            .OrderByDescending(r => r.ConfidenceScore)
            .ThenByDescending(r => r.ReportedAt)
            .Take(limit)
            .ToListAsync();

        return reports.Select(r => r.ToDto()).ToList();
    }
}
