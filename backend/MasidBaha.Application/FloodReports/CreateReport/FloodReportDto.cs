using System.Text.Json.Serialization;
using FluentValidation;
using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.CreateReport;

public class FloodReportDto
{
    public Guid Id { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime ReportedAt { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}

public class CreateFloodReportRequest
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }

    // Set by the server from the signed session cookie. Anything a client
    // sends here is ignored, because a client-chosen id is trivially reset.
    public string ReporterSessionId { get; set; } = string.Empty;

    // Also set by the server, never read from the request body
    [JsonIgnore] public string? ReporterIpHash { get; set; }

    // True when the reporter is shadow-banned: saved, but never shown
    [JsonIgnore] public bool IsHidden { get; set; }
}

public static class PhilippinesBounds
{
    // Generous box around the whole archipelago (includes the Batanes and Tawi-Tawi extremes)
    public const double MinLat = 4.0, MaxLat = 21.5;
    public const double MinLng = 116.0, MaxLng = 127.0;
}

public class CreateFloodReportRequestValidator : AbstractValidator<CreateFloodReportRequest>
{
    public CreateFloodReportRequestValidator()
    {
        // Geofence: reports are only meaningful (and only geocodable) inside the Philippines
        RuleFor(x => x.Lat)
            .InclusiveBetween(PhilippinesBounds.MinLat, PhilippinesBounds.MaxLat)
            .WithMessage("Reports must be located within the Philippines.");

        RuleFor(x => x.Lng)
            .InclusiveBetween(PhilippinesBounds.MinLng, PhilippinesBounds.MaxLng)
            .WithMessage("Reports must be located within the Philippines.");

        RuleFor(x => x.Severity).IsInEnum().WithMessage("Invalid severity value.");

        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Notes cannot exceed 500 characters.");

        RuleFor(x => x.PhotoUrl).MaximumLength(500);
    }
}
