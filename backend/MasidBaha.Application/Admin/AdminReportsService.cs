using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Enums;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.Admin;

public interface IAdminReportsService
{
    Task<AdminGetReportsResult> GetReportsAsync(AdminGetReportsQuery query);
    Task<AdminStatusResult> SetStatusAsync(Guid floodReportId, ReportStatus status);
    Task<bool> DeleteAsync(Guid floodReportId);
}

public class AdminReportsService : IAdminReportsService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public AdminReportsService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AdminGetReportsResult> GetReportsAsync(AdminGetReportsQuery query)
    {
        var reports = new List<AdminFloodReportDto>();
        var totalCount = 0;

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_AdminGetReports", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@Status", query.Status.HasValue ? (byte)query.Status.Value : DBNull.Value);
        command.Parameters.AddWithValue("@Page", query.Page);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();

        // First result set: the page of reports.
        while (await reader.ReadAsync())
        {
            reports.Add(new AdminFloodReportDto
            {
                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                Lat = reader.GetDouble(reader.GetOrdinal("Lat")),
                Lng = reader.GetDouble(reader.GetOrdinal("Lng")),
                Severity = (Severity)reader.GetByte(reader.GetOrdinal("Severity")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                PhotoUrl = reader.IsDBNull(reader.GetOrdinal("PhotoUrl")) ? null : reader.GetString(reader.GetOrdinal("PhotoUrl")),
                ReportedAt = reader.GetDateTime(reader.GetOrdinal("ReportedAt")),
                ExpiresAt = reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
                ConfidenceScore = reader.GetInt32(reader.GetOrdinal("ConfidenceScore")),
                Status = (ReportStatus)reader.GetByte(reader.GetOrdinal("Status")),
                ReporterSessionId = reader.GetString(reader.GetOrdinal("ReporterSessionId")),
                Region = reader.IsDBNull(reader.GetOrdinal("Region")) ? null : reader.GetString(reader.GetOrdinal("Region")),
                Province = reader.IsDBNull(reader.GetOrdinal("Province")) ? null : reader.GetString(reader.GetOrdinal("Province")),
                City = reader.IsDBNull(reader.GetOrdinal("City")) ? null : reader.GetString(reader.GetOrdinal("City"))
            });
        }

        // Second result set: total count, for pagination controls on the admin UI.
        if (await reader.NextResultAsync() && await reader.ReadAsync())
        {
            totalCount = reader.GetInt32(reader.GetOrdinal("TotalCount"));
        }

        return new AdminGetReportsResult
        {
            Reports = reports,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<AdminStatusResult> SetStatusAsync(Guid floodReportId, ReportStatus status)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_AdminSetReportStatus", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@FloodReportId", floodReportId);
        command.Parameters.AddWithValue("@Status", (byte)status);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Report not found.");

        return new AdminStatusResult
        {
            Status = (ReportStatus)reader.GetByte(reader.GetOrdinal("Status")),
            ConfidenceScore = reader.GetInt32(reader.GetOrdinal("ConfidenceScore"))
        };
    }

    public async Task<bool> DeleteAsync(Guid floodReportId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_AdminDeleteReport", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@FloodReportId", floodReportId);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return false;

        return reader.GetInt32(reader.GetOrdinal("DeletedCount")) > 0;
    }
}
