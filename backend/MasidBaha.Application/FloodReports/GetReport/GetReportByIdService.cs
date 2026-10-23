using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.FloodReports.GetReport;

public interface IGetReportByIdService
{
    Task<FloodReportDto> GetAsync(Guid id, string? viewerSessionId = null);
}

public class GetReportByIdService : IGetReportByIdService
{
    private readonly MasidBahaDbContext _db;

    public GetReportByIdService(MasidBahaDbContext db)
    {
        _db = db;
    }

    // Any status is returned: closed reports are already public through the heatmap. A hidden report
    // (pulled by staff, or from a shadow-banned source) is a 404 for everyone except the person who
    // sent it, so a ban never shows itself and a share link can never expose it.
    public async Task<FloodReportDto> GetAsync(Guid id, string? viewerSessionId = null)
    {
        var report = await _db.FloodReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new NotFoundException("Report not found.");

        if (report.IsHidden && (viewerSessionId is null || report.ReporterSessionId != viewerSessionId))
            throw new NotFoundException("Report not found.");

        return report.ToDto();
    }
}
