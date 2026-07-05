using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports.CreateReport;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.GetNearbyReports;

public interface IGetNearbyReportsService
{
    Task<List<FloodReportDto>> GetNearbyAsync(NearbyReportsQuery query);
}

public class GetNearbyReportsService : IGetNearbyReportsService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public GetNearbyReportsService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<FloodReportDto>> GetNearbyAsync(NearbyReportsQuery query)
    {
        var results = new List<FloodReportDto>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_GetNearbyReports", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@Lat", query.Lat);
        command.Parameters.AddWithValue("@Lng", query.Lng);
        command.Parameters.AddWithValue("@RadiusMeters", query.RadiusMeters);

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
                ReportedAt = reader.GetDateTime(reader.GetOrdinal("ReportedAt")),
                ConfidenceScore = reader.GetInt32(reader.GetOrdinal("ConfidenceScore")),
                Status = (ReportStatus)reader.GetByte(reader.GetOrdinal("Status"))
            });
        }

        return results;
    }
}