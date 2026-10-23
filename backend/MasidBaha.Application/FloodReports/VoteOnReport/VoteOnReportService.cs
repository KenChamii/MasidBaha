using System.Data;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Exceptions;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public interface IVoteOnReportService
{
    Task<VoteResultDto> VoteAsync(Guid floodReportId, VoteRequest request);
}

public class VoteOnReportService : IVoteOnReportService
{
    // Custom error numbers raised by sp_VoteOnReport via THROW
    private const int ReportNotFound = 50404;
    private const int AlreadyVoted = 50409;
    private const int ReportClosed = 50410;
    // SQL Server unique-constraint violation codes, kept as a backstop for races
    private const int UniqueConstraint = 2627;
    private const int UniqueIndex = 2601;

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
        command.Parameters.Add("@VoteType", SqlDbType.SmallInt).Value = request.VoteType.ToDelta();

        try
        {
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
        catch (SqlException ex) when (ex.Number == ReportNotFound)
        {
            throw new NotFoundException("Report not found.");
        }
        catch (SqlException ex) when (ex.Number == ReportClosed)
        {
            throw new ConflictException("This report is no longer active.");
        }
        catch (SqlException ex) when (ex.Number is AlreadyVoted or UniqueConstraint or UniqueIndex)
        {
            throw new ConflictException("You have already voted on this report.");
        }
    }
}
