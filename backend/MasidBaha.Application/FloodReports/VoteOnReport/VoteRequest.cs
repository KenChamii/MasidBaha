using FluentValidation;
using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public class VoteRequest
{
    // Set by the server from the signed session cookie; a client-sent value is ignored
    public string VoterSessionId { get; set; } = string.Empty;

    public VoteType VoteType { get; set; }
}

public class VoteRequestValidator : AbstractValidator<VoteRequest>
{
    public VoteRequestValidator()
    {
        RuleFor(x => x.VoteType).IsInEnum().WithMessage("Invalid vote type.");
    }
}

public class VoteResultDto
{
    public Guid FloodReportId { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
}
