using System.Globalization;
using System.Text;
using Asp.Versioning;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Trust;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.Controllers;

// Back-office endpoints. Everything needs a signed-in staff account; the policies
// below decide who may do what (Viewer reads, Moderator acts, SuperAdmin manages)
[ApiController]
[ApiVersion("1.0")]
[Authorize(Policy = AdminPolicies.CanView)]
[Route("api/v{version:apiVersion}/admin")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class AdminController : ControllerBase
{
    private readonly IAdminReportsService _adminReportsService;
    private readonly ISessionTrustService _sessionTrustService;
    private readonly IOutputCacheStore _cache;
    private readonly IHubContext<FloodHub> _hubContext;

    public AdminController(
        IAdminReportsService adminReportsService,
        ISessionTrustService sessionTrustService,
        IOutputCacheStore cache,
        IHubContext<FloodHub> hubContext)
    {
        _adminReportsService = adminReportsService;
        _sessionTrustService = sessionTrustService;
        _cache = cache;
        _hubContext = hubContext;
    }

    // Lets an admin check how reliable a reporting session has been before
    // deciding whether to trust a specific report from it.
    /// <summary>How reliable a reporting session has been, based on its report history.</summary>
    [HttpGet("sessions/{sessionId}/trust")]
    [ProducesResponseType<SessionTrustDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SessionTrustDto>> GetSessionTrust(string sessionId)
    {
        return Ok(await _sessionTrustService.GetTrustScoreAsync(sessionId));
    }

    /// <summary>Paged list of reports of any status.</summary>
    [HttpGet("reports")]
    [ProducesResponseType<AdminGetReportsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminGetReportsResult>> GetReports([FromQuery] AdminGetReportsQuery query)
    {
        return Ok(await _adminReportsService.GetReportsAsync(query));
    }

    /// <summary>Everything staff need to judge one report: votes, reporter trust, flags, bans and history.</summary>
    [HttpGet("reports/{id:guid}")]
    [ProducesResponseType<AdminReportDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminReportDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _adminReportsService.GetDetailAsync(id, cancellationToken));
    }

    /// <summary>Lightweight points for the admin map, using the same filters as the table.</summary>
    [HttpGet("reports/map")]
    [ProducesResponseType<List<AdminMapPointDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AdminMapPointDto>>> GetMap([FromQuery] AdminReportFilter filter, CancellationToken cancellationToken)
    {
        return Ok(await _adminReportsService.GetMapPointsAsync(filter, cancellationToken));
    }

    /// <summary>Downloads the filtered reports as CSV (the export is recorded in the audit log).</summary>
    [HttpGet("reports/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task Export([FromQuery] AdminReportFilter filter, CancellationToken cancellationToken)
    {
        var rows = await _adminReportsService.ExportAsync(filter, cancellationToken);

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"masidbaha-reports-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv\"";

        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync("id,reported_at,expires_at,status,severity,confidence,lat,lng,region,province,city,hidden,notes,reporter_session,reporter_ip_fingerprint");
        await foreach (var r in rows.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(string.Join(',',
                r.Id, r.ReportedAt.ToString("o"), r.ExpiresAt.ToString("o"), r.Status, r.Severity, r.ConfidenceScore,
                r.Lat.ToString(CultureInfo.InvariantCulture), r.Lng.ToString(CultureInfo.InvariantCulture),
                Csv(r.Region), Csv(r.Province), Csv(r.City), r.IsHidden, Csv(r.Notes), Csv(r.ReporterSessionId), Csv(r.ReporterIpFingerprint)));
        }
    }

    // Quotes a field and defuses cells a spreadsheet would run as a formula (notes are user-written)
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var text = value.Replace("\r", " ").Replace("\n", " ");
        if (text[0] is '=' or '+' or '-' or '@' or '\t') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Pulls a report off the public map (or puts it back) without deleting it.</summary>
    [HttpPatch("reports/{id:guid}/hidden")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetHidden(Guid id, AdminSetHiddenRequest request, CancellationToken cancellationToken)
    {
        if (!await _adminReportsService.SetHiddenAsync(id, request.Hidden, cancellationToken))
            return Problem(detail: "Report not found.", statusCode: StatusCodes.Status404NotFound);

        await _cache.EvictByTagAsync(FloodReportsController.CacheTag, cancellationToken);
        if (request.Hidden)
            await _hubContext.Clients.All.SendAsync("RemoveReport", id, cancellationToken);
        return NoContent();
    }

    // "Verify" = force status to Resolved/Active/Expired directly, bypassing
    // the normal vote-driven flow, for when a report is obviously
    // legit/fake and shouldn't wait on community votes to resolve.
    /// <summary>Forces a report's status, bypassing community votes.</summary>
    [HttpPatch("reports/{id:guid}/status")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetStatus(Guid id, AdminSetStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _adminReportsService.SetStatusAsync(id, request.Status);

        await _cache.EvictByTagAsync(FloodReportsController.CacheTag, cancellationToken);
        await _hubContext.Clients.All.SendAsync("ReportUpdated", new
        {
            FloodReportId = id,
            result.ConfidenceScore,
            result.Status
        }, cancellationToken);

        if (result.Status != ReportStatus.Active)
            await _hubContext.Clients.All.SendAsync("RemoveReport", id, cancellationToken);

        return NoContent();
    }

    /// <summary>Permanently deletes a report and its votes (spam or fake).</summary>
    [HttpDelete("reports/{id:guid}")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await _adminReportsService.DeleteAsync(id))
            return Problem(detail: "Report not found.", statusCode: StatusCodes.Status404NotFound);

        await _cache.EvictByTagAsync(FloodReportsController.CacheTag, cancellationToken);
        await _hubContext.Clients.All.SendAsync("RemoveReport", id, cancellationToken);
        return NoContent();
    }
}
