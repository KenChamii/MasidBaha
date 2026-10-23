using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Persistence;

namespace MasidBaha.Application.FloodReports.CreateReport;

public interface ICreateFloodReportService
{
    Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request);
}

public class CreateFloodReportService : ICreateFloodReportService
{
    private readonly MasidBahaDbContext _db;
    private readonly IGeocodingService _geocodingService;

    public CreateFloodReportService(MasidBahaDbContext db, IGeocodingService geocodingService)
    {
        _db = db;
        _geocodingService = geocodingService;
    }

    public async Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request)
    {
        var address = await _geocodingService.ReverseGeocodeAsync(request.Lat, request.Lng);
        var now = DateTime.UtcNow;

        var report = new FloodReport
        {
            Id = Guid.CreateVersion7(),
            Location = FloodReportMappings.ToPoint(request.Lat, request.Lng),
            Severity = request.Severity,
            // Stored raw on purpose: encoding belongs on output (Angular escapes interpolated
            // values), and encoding here would double-encode in the UI.
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            PhotoUrl = request.PhotoUrl,
            ReportedAt = now,
            ExpiresAt = now + ReportDefaults.Lifetime,
            ConfidenceScore = 1,
            Status = ReportStatus.Active,
            ReporterSessionId = request.ReporterSessionId,
            Region = address.Region,
            Province = address.Province,
            City = address.City
        };

        _db.FloodReports.Add(report);
        await _db.SaveChangesAsync();

        return report.ToDto();
    }
}
