using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Enums;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.GetHeatmapData;

public interface IGetHeatmapDataService
{
    Task<List<HeatmapPointDto>> GetHeatmapAsync(HeatmapQuery query);
}

public class GetHeatmapDataService : IGetHeatmapDataService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public GetHeatmapDataService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<HeatmapPointDto>> GetHeatmapAsync(HeatmapQuery query)
    {
        var results = new List<HeatmapPointDto>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_GetHeatmapData", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@FromDate", (object?)query.FromDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@ToDate", (object?)query.ToDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@Region", (object?)query.Region ?? DBNull.Value);
        command.Parameters.AddWithValue("@Province", (object?)query.Province ?? DBNull.Value);
        command.Parameters.AddWithValue("@City", (object?)query.City ?? DBNull.Value);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new HeatmapPointDto
            {
                Lat = reader.GetDouble(reader.GetOrdinal("Lat")),
                Lng = reader.GetDouble(reader.GetOrdinal("Lng")),
                Severity = (Severity)reader.GetByte(reader.GetOrdinal("Severity")),
                Status = (ReportStatus)reader.GetByte(reader.GetOrdinal("Status")),
                ReportedAt = reader.GetDateTime(reader.GetOrdinal("ReportedAt")),
                Region = reader.IsDBNull(reader.GetOrdinal("Region")) ? null : reader.GetString(reader.GetOrdinal("Region")),
                Province = reader.IsDBNull(reader.GetOrdinal("Province")) ? null : reader.GetString(reader.GetOrdinal("Province")),
                City = reader.IsDBNull(reader.GetOrdinal("City")) ? null : reader.GetString(reader.GetOrdinal("City"))
            });
        }

        return results;
    }
}
