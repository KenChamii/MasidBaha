using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Enums;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public interface IVoteOnReportService
{
    Task<VoteResultDto> VoteAsync(Guid floodReportId, VoteRequest request);
}

public class VoteOnReportService : IVoteOnReportService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public VoteOnReportService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<VoteResultDto> VoteAsync(Guid floodReportId, VoteRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_VoteOnReport", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@FloodReportId", floodReportId);
        command.Parameters.AddWithValue("@VoterSessionId", request.VoterSessionId);
        command.Parameters.AddWithValue("@VoteType", (byte)request.VoteType);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Vote did not return expected result");

        return new VoteResultDto
        {
            FloodReportId = floodReportId,
            ConfidenceScore = reader.GetInt32(reader.GetOrdinal("ConfidenceScore")),
            Status = (ReportStatus)reader.GetByte(reader.GetOrdinal("Status"))
        };
    }
}