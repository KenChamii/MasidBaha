using MasidBaha.Application.Admin;
using MasidBaha.Application.FloodReports.CreateReport;
using NetTopologySuite.Geometries;

namespace MasidBaha.Application.Persistence;

public static class FloodReportMappings
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), MasidBahaDbContext.Wgs84Srid);

    // PostGIS point order is (longitude, latitude), the reverse of how callers think
    public static Point ToPoint(double lat, double lng) => Factory.CreatePoint(new Coordinate(lng, lat));

    public static FloodReportDto ToDto(this FloodReport r) => new()
    {
        Id = r.Id,
        Lat = r.Location.Y,
        Lng = r.Location.X,
        Severity = r.Severity,
        Notes = r.Notes,
        PhotoUrl = r.PhotoUrl,
        ReportedAt = r.ReportedAt,
        ConfidenceScore = r.ConfidenceScore,
        Status = r.Status,
        Region = r.Region,
        Province = r.Province,
        City = r.City
    };

    public static AdminFloodReportDto ToAdminDto(this FloodReport r) => new()
    {
        Id = r.Id,
        Lat = r.Location.Y,
        Lng = r.Location.X,
        Severity = r.Severity,
        Notes = r.Notes,
        PhotoUrl = r.PhotoUrl,
        ReportedAt = r.ReportedAt,
        ExpiresAt = r.ExpiresAt,
        ConfidenceScore = r.ConfidenceScore,
        Status = r.Status,
        ReporterSessionId = r.ReporterSessionId,
        ReporterIpFingerprint = r.ReporterIpHash is null ? null : r.ReporterIpHash[..Math.Min(10, r.ReporterIpHash.Length)],
        IsHidden = r.IsHidden,
        Region = r.Region,
        Province = r.Province,
        City = r.City
    };

    // Npgsql only accepts Kind=Utc for timestamptz. Query-string dates arrive
    // as Unspecified, so treat those as UTC rather than letting the write fail.
    public static DateTime ToUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
