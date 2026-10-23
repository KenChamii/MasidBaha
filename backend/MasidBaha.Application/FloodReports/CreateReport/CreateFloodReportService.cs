using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Geocoding;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.CreateReport;

public interface ICreateFloodReportService
{
    Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request);
}

public class CreateFloodReportService : ICreateFloodReportService
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IGeocodingService _geocodingService;

    public CreateFloodReportService(ISqlConnectionFactory connectionFactory, IGeocodingService geocodingService)
    {
        _connectionFactory = connectionFactory;
        _geocodingService = geocodingService;
    }

    public async Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request)
    {
        var address = await _geocodingService.ReverseGeocodeAsync(request.Lat, request.Lng);

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_InsertFloodReport", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        // Stored raw on purpose: encoding belongs on output (Angular escapes interpolated
        // values), and encoding here would double-encode in the UI.
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        command.Parameters.AddWithValue("@Lat", request.Lat);
        command.Parameters.AddWithValue("@Lng", request.Lng);
        command.Parameters.AddWithValue("@Severity", (byte)request.Severity);
        command.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@PhotoUrl", (object?)request.PhotoUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReporterSessionId", request.ReporterSessionId);
        command.Parameters.AddWithValue("@Region", (object?)address.Region ?? DBNull.Value);
        command.Parameters.AddWithValue("@Province", (object?)address.Province ?? DBNull.Value);
        command.Parameters.AddWithValue("@City", (object?)address.City ?? DBNull.Value);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Insert did not return expected result");

        return new FloodReportDto
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            Lat = request.Lat,
            Lng = request.Lng,
            Severity = request.Severity,
            Notes = notes,
            PhotoUrl = request.PhotoUrl,
            ReportedAt = reader.GetUtcDateTime(reader.GetOrdinal("ReportedAt")),
            ConfidenceScore = reader.GetInt32(reader.GetOrdinal("ConfidenceScore")),
            Status = (Common.Enums.ReportStatus)reader.GetByte(reader.GetOrdinal("Status")),
            Region = address.Region,
            Province = address.Province,
            City = address.City
        };
    }
}