using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.FloodReports.GetReport;

public interface IGetReportByIdService
{
    Task<FloodReportDto> GetAsync(Guid id);
}

public class GetReportByIdService : IGetReportByIdService
{
    private readonly MasidBahaDbContext _db;

    public GetReportByIdService(MasidBahaDbContext db)
    {
        _db = db;
    }

    // Any status is returned: closed reports are already public through the heatmap
    public async Task<FloodReportDto> GetAsync(Guid id)
    {
        var report = await _db.FloodReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new NotFoundException("Report not found.");

        return report.ToDto();
    }
}
