using Asp.Versioning;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Trust;
using MasidBaha.WebAPI.Hubs;
using MasidBaha.WebAPI.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.Controllers;

// Everything here requires the X-Admin-Key header (see AdminApiKeyAttribute)
[ApiController]
[ApiVersion("1.0")]
[AdminApiKey]
[Route("api/v{version:apiVersion}/admin")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
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

    // "Verify" = force status to Resolved/Active/Expired directly, bypassing
    // the normal vote-driven flow, for when a report is obviously
    // legit/fake and shouldn't wait on community votes to resolve.
    /// <summary>Forces a report's status, bypassing community votes.</summary>
    [HttpPatch("reports/{id:guid}/status")]
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
