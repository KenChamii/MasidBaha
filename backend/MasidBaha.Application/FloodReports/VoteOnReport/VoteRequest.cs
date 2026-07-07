using System.ComponentModel.DataAnnotations;
using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public class VoteRequest
{
    [Required(ErrorMessage = "VoterSessionId is required.")]
    [MaxLength(100)]
    public string VoterSessionId { get; set; } = string.Empty;

    [EnumDataType(typeof(VoteType), ErrorMessage = "Invalid vote type.")]
    public VoteType VoteType { get; set; }
}

public class VoteResultDto
{
    public Guid FloodReportId { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
}