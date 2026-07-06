using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public class VoteRequest
{
    public string VoterSessionId { get; set; } = string.Empty;
    public VoteType VoteType { get; set; }
}

public class VoteResultDto
{
    public Guid FloodReportId { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
}