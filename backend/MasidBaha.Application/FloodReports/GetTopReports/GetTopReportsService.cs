using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports.CreateReport;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.GetTopReports;

public interface IGetTopReportsService
{
    Task<List<FloodReportDto>> GetTopAsync(TopReportsQuery query);
}

public class GetTopReportsService : IGetTopReportsService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public GetTopReportsService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<FloodReportDto>> GetTopAsync(TopReportsQuery query)
    {
        var results = new List<FloodReportDto>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_GetTopReports", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@Region", (object?)query.Region ?? DBNull.Value);
        command.Parameters.AddWithValue("@Province", (object?)query.Province ?? DBNull.Value);
        command.Parameters.AddWithValue("@City", (object?)query.City ?? DBNull.Value);
        command.Parameters.AddWithValue("@Limit", query.Limit);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new FloodReportDto
            {
                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                Lat = reader.GetDouble(reader.GetOrdinal("Lat")),
                Lng = reader.GetDouble(reader.GetOrdinal("Lng")),
                Severity = (Severity)reader.GetByte(reader.GetOrdinal("Severity")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                PhotoUrl = reader.IsDBNull(reader.GetOrdinal("PhotoUrl")) ? null : reader.GetString(reader.GetOrdinal("PhotoUrl")),
                ReportedAt = reader.GetUtcDateTime(reader.GetOrdinal("ReportedAt")),
                ConfidenceScore = reader.GetInt32(reader.GetOrdinal("ConfidenceScore")),
                Status = (ReportStatus)reader.GetByte(reader.GetOrdinal("Status")),
                Region = reader.IsDBNull(reader.GetOrdinal("Region")) ? null : reader.GetString(reader.GetOrdinal("Region")),
                Province = reader.IsDBNull(reader.GetOrdinal("Province")) ? null : reader.GetString(reader.GetOrdinal("Province")),
                City = reader.IsDBNull(reader.GetOrdinal("City")) ? null : reader.GetString(reader.GetOrdinal("City"))
            });
        }

        return results;
    }
}
