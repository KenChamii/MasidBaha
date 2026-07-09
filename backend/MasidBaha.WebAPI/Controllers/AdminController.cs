using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Trust;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.Controllers;

// Everything under /api/admin/** is gated by AdminAuthMiddleware (checks the
// X-Admin-Key header) 
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminReportsService _adminReportsService;
    private readonly ISessionTrustService _sessionTrustService;
    private readonly IHubContext<FloodHub> _hubContext;

    public AdminController(
        IAdminReportsService adminReportsService,
        ISessionTrustService sessionTrustService,
        IHubContext<FloodHub> hubContext)
    {
        _adminReportsService = adminReportsService;
        _sessionTrustService = sessionTrustService;
        _hubContext = hubContext;
    }

    // Lets an admin check how reliable a reporting session has been before
    // deciding whether to trust a specific report from it.
    [HttpGet("sessions/{sessionId}/trust")]
    public async Task<ActionResult<SessionTrustDto>> GetSessionTrust(string sessionId)
    {
        var trust = await _sessionTrustService.GetTrustScoreAsync(sessionId);
        return Ok(trust);
    }

    [HttpGet("reports")]
    public async Task<ActionResult<AdminGetReportsResult>> GetReports([FromQuery] AdminGetReportsQuery query)
    {
        var result = await _adminReportsService.GetReportsAsync(query);
        return Ok(result);
    }

    // "Verify" = force status to Resolved/Active/Expired directly, bypassing
    // the normal vote-driven flow — for when a report is obviously
    // legit/fake and shouldn't wait on community votes to resolve.
    [HttpPatch("reports/{id}/status")]
    public async Task<ActionResult> SetStatus(Guid id, AdminSetStatusRequest request)
    {
        var result = await _adminReportsService.SetStatusAsync(id, request.Status);

        await _hubContext.Clients.All.SendAsync("ReportUpdated", new
        {
            FloodReportId = id,
            ConfidenceScore = result.ConfidenceScore,
            Status = result.Status
        });

        if (result.Status != ReportStatus.Active)
            await _hubContext.Clients.All.SendAsync("RemoveReport", id);

        return NoContent();
    }

    [HttpDelete("reports/{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var deleted = await _adminReportsService.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        await _hubContext.Clients.All.SendAsync("RemoveReport", id);
        return NoContent();
    }
}
