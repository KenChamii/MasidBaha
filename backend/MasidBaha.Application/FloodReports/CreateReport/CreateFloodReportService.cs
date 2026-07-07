using System.Data;
using System.Net;
using MasidBaha.Application.Common.Data;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.CreateReport;

public interface ICreateFloodReportService
{
    Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request);
}

public class CreateFloodReportService : ICreateFloodReportService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public CreateFloodReportService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FloodReportDto> CreateAsync(CreateFloodReportRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_InsertFloodReport", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        // Encode before storing — Notes may eventually be rendered as raw HTML
        // in a popup (see frontend MapPageComponent), so we don't want stored XSS.
        var encodedNotes = string.IsNullOrWhiteSpace(request.Notes)
            ? null
            : WebUtility.HtmlEncode(request.Notes.Trim());

        command.Parameters.AddWithValue("@Lat", request.Lat);
        command.Parameters.AddWithValue("@Lng", request.Lng);
        command.Parameters.AddWithValue("@Severity", (byte)request.Severity);
        command.Parameters.AddWithValue("@Notes", (object?)encodedNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("@PhotoUrl", (object?)request.PhotoUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReporterSessionId", request.ReporterSessionId);

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
            Notes = encodedNotes,
            PhotoUrl = request.PhotoUrl,
            ReportedAt = DateTime.UtcNow,
            ConfidenceScore = 1,
            Status = Common.Enums.ReportStatus.Active
        };
    }
}