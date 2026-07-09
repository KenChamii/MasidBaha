using System.Data;
using MasidBaha.Application.Common.Data;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.Trust;

public class SessionTrustDto
{
    public string SessionId { get; set; } = string.Empty;
    public int TotalReports { get; set; }
    public int ResolvedReports { get; set; }
    public int ActiveReports { get; set; }
    public double AvgConfidenceScore { get; set; }
    public DateTime? FirstReportAt { get; set; }
    public DateTime? LastReportAt { get; set; }

    // 0-100, or null for a session with no report history yet ("Unrated"
    // rather than assuming either trustworthy or suspicious by default).
    // Deliberately simple and explainable for an admin glancing at it: what
    // share of this session's reports the community ended up confirming
    // (Status = Resolved) rather than letting expire untouched.
    public int? TrustScore =>
        TotalReports == 0 ? null : (int)Math.Round((double)ResolvedReports / TotalReports * 100);
}

public interface ISessionTrustService
{
    Task<SessionTrustDto> GetTrustScoreAsync(string sessionId);
}

public class SessionTrustService : ISessionTrustService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SessionTrustService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SessionTrustDto> GetTrustScoreAsync(string sessionId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_GetSessionTrustScore", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@SessionId", sessionId);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new SessionTrustDto { SessionId = sessionId };

        return new SessionTrustDto
        {
            SessionId = sessionId,
            TotalReports = reader.GetInt32(reader.GetOrdinal("TotalReports")),
            ResolvedReports = reader.GetInt32(reader.GetOrdinal("ResolvedReports")),
            ActiveReports = reader.GetInt32(reader.GetOrdinal("ActiveReports")),
            AvgConfidenceScore = reader.GetDouble(reader.GetOrdinal("AvgConfidenceScore")),
            FirstReportAt = reader.GetUtcDateTimeOrNull(reader.GetOrdinal("FirstReportAt")),
            LastReportAt = reader.GetUtcDateTimeOrNull(reader.GetOrdinal("LastReportAt"))
        };
    }
}
