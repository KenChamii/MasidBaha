using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Common;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Trust;

namespace MasidBaha.Application.Admin;

// Fuller than the public FloodReportDto on purpose: staff reviewing for
// spam/fake reports need the reporter session (to spot one session flooding
// the map with junk pins), the expiry time and the moderation state, none of
// which the public API exposes.
public class AdminFloodReportDto
{
    public Guid Id { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime ReportedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
    public string ReporterSessionId { get; set; } = string.Empty;

    // Only the start of the keyed IP hash: enough to tell sources apart, useless as an address
    public string? ReporterIpFingerprint { get; set; }

    public bool IsHidden { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}

// Filters shared by the table, the map and the CSV export
public class AdminReportFilter : PagedQuery
{
    public string? Search { get; set; }
    public ReportStatus? Status { get; set; }
    public Severity? Severity { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool? Hidden { get; set; }
}

public class AdminGetReportsQuery : AdminReportFilter
{
    // reportedAt (default), confidence, severity, status, expiresAt
    public string? SortBy { get; set; }
    public bool Descending { get; set; } = true;
}

public class AdminGetReportsResult
{
    public List<AdminFloodReportDto> Reports { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class AdminMapPointDto
{
    public Guid Id { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public ReportStatus Status { get; set; }
    public int ConfidenceScore { get; set; }
    public DateTime ReportedAt { get; set; }
    public string? City { get; set; }
    public bool IsHidden { get; set; }
}

public class AdminVoteDto
{
    public string VoterSessionId { get; set; } = string.Empty;
    public VoteType VoteType { get; set; }
    public DateTime VotedAt { get; set; }
}

public class AdminReportDetailDto
{
    public AdminFloodReportDto Report { get; set; } = new();
    public List<AdminVoteDto> Votes { get; set; } = new();
    public SessionTrustDto ReporterTrust { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public List<BanDto> ActiveBans { get; set; } = new();
    public List<AuditEntryDto> History { get; set; } = new();
}

public class AdminSetStatusRequest
{
    public ReportStatus Status { get; set; }
}

public class AdminSetHiddenRequest
{
    public bool Hidden { get; set; }
}

public class AdminStatusResult
{
    public ReportStatus Status { get; set; }
    public int ConfidenceScore { get; set; }
}

public class AdminReportFilterValidator : AbstractValidator<AdminReportFilter>
{
    public AdminReportFilterValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.Province).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Severity).IsInEnum().When(x => x.Severity.HasValue);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("To must not be earlier than From.");
    }
}

public class AdminGetReportsQueryValidator : AbstractValidator<AdminGetReportsQuery>
{
    private static readonly string[] SortFields = ["reportedAt", "confidence", "severity", "status", "expiresAt"];

    public AdminGetReportsQueryValidator()
    {
        Include(new AdminReportFilterValidator());
        RuleFor(x => x.SortBy)
            .Must(s => s is null || SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortFields)}.");
    }
}

public class AdminSetStatusRequestValidator : AbstractValidator<AdminSetStatusRequest>
{
    public AdminSetStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
