using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MasidBaha.Application.FloodReports.CreateReport;

// WasAlreadySaved: this exact request was made before, so the first report is returned and nothing new happened
public record CreateReportResult(FloodReportDto Report, bool WasAlreadySaved);

public interface ICreateFloodReportService
{
    Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request);

    // Like CreateAsync, but says whether the report is new (the caller broadcasts and notifies only for new ones)
    Task<CreateReportResult> CreateOrGetAsync(CreateFloodReportRequest request);
}

public class CreateFloodReportService : ICreateFloodReportService
{
    private readonly MasidBahaDbContext _db;
    private readonly IGeocodingService _geocodingService;
    private readonly IRuntimeSettings _settings;

    public CreateFloodReportService(MasidBahaDbContext db, IGeocodingService geocodingService, IRuntimeSettings settings)
    {
        _db = db;
        _geocodingService = geocodingService;
        _settings = settings;
    }

    public async Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request) =>
        (await CreateOrGetAsync(request)).Report;

    public async Task<CreateReportResult> CreateOrGetAsync(CreateFloodReportRequest request)
    {
        if (request.ClientRequestId is { } requestId && await FindEarlierAsync(requestId) is { } earlier)
            return new CreateReportResult(earlier.ToDto(), true);

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
            ExpiresAt = now + _settings.Current.ReportLifetime,
            ConfidenceScore = 1,
            Status = ReportStatus.Active,
            ReporterSessionId = request.ReporterSessionId,
            ClientRequestId = request.ClientRequestId,
            ReporterIpHash = request.ReporterIpHash,
            IsHidden = request.IsHidden,
            Region = address.Region,
            Province = address.Province,
            City = address.City
        };

        _db.FloodReports.Add(report);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (request.ClientRequestId is not null && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same request arrived twice at once and the other one won: hand back its report
            _db.Entry(report).State = EntityState.Detached;
            var winner = await FindEarlierAsync(request.ClientRequestId.Value);
            if (winner is not null) return new CreateReportResult(winner.ToDto(), true);
            throw;
        }

        return new CreateReportResult(report.ToDto(), false);
    }

    private Task<FloodReport?> FindEarlierAsync(Guid clientRequestId) =>
        _db.FloodReports.AsNoTracking().FirstOrDefaultAsync(r => r.ClientRequestId == clientRequestId);
}
