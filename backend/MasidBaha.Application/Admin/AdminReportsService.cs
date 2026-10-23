using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin;

public interface IAdminReportsService
{
    Task<AdminGetReportsResult> GetReportsAsync(AdminGetReportsQuery query);
    Task<AdminStatusResult> SetStatusAsync(Guid floodReportId, ReportStatus status);
    Task<bool> DeleteAsync(Guid floodReportId);
}

public class AdminReportsService : IAdminReportsService
{
    private const int MaxPageSize = 100;

    private readonly MasidBahaDbContext _db;

    public AdminReportsService(MasidBahaDbContext db)
    {
        _db = db;
    }

    public async Task<AdminGetReportsResult> GetReportsAsync(AdminGetReportsQuery query)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var reports = _db.FloodReports.AsNoTracking()
            .Where(r => query.Status == null || r.Status == query.Status);

        var totalCount = await reports.CountAsync();

        var items = await reports
            .OrderByDescending(r => r.ReportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new AdminGetReportsResult
        {
            Reports = items.Select(r => r.ToAdminDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AdminStatusResult> SetStatusAsync(Guid floodReportId, ReportStatus status)
    {
        var report = await _db.FloodReports.FirstOrDefaultAsync(r => r.Id == floodReportId)
            ?? throw new NotFoundException("Report not found.");

        report.Status = status;
        await _db.SaveChangesAsync();

        return new AdminStatusResult
        {
            Status = report.Status,
            ConfidenceScore = report.ConfidenceScore
        };
    }

    // Votes go with the report through the FK's ON DELETE CASCADE
    public async Task<bool> DeleteAsync(Guid floodReportId)
    {
        var deleted = await _db.FloodReports.Where(r => r.Id == floodReportId).ExecuteDeleteAsync();
        return deleted > 0;
    }
}
