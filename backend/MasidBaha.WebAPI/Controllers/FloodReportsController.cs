using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.Common.Enums;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[Route("api/flood-reports")]
public class FloodReportsController : ControllerBase
{
    private readonly ICreateFloodReportService _createService;
    private readonly IGetNearbyReportsService _nearbyService;
    private readonly IGetTopReportsService _topService;
    private readonly IVoteOnReportService _voteService;
    private readonly IHubContext<FloodHub> _hubContext;

    public FloodReportsController(
        ICreateFloodReportService createService,
        IGetNearbyReportsService nearbyService,
        IGetTopReportsService topService,
        IVoteOnReportService voteService,
        IHubContext<FloodHub> hubContext)
    {
        _createService = createService;
        _nearbyService = nearbyService;
        _topService = topService;
        _voteService = voteService;
        _hubContext = hubContext;
    }

    [HttpPost]
    [EnableRateLimiting("report-writes")]
    public async Task<ActionResult<FloodReportDto>> Create(CreateFloodReportRequest request)
    {
        var report = await _createService.CreateAsync(request);
        await _hubContext.Clients.All.SendAsync("NewReport", report);
        return Ok(report);
    }

    [HttpGet]
    public async Task<ActionResult<List<FloodReportDto>>> GetNearby([FromQuery] NearbyReportsQuery query)
    {
        var reports = await _nearbyService.GetNearbyAsync(query);
        return Ok(reports);
    }

    [HttpGet("top")]
    public async Task<ActionResult<List<FloodReportDto>>> GetTop([FromQuery] TopReportsQuery query)
    {
        var reports = await _topService.GetTopAsync(query);
        return Ok(reports);
    }

    [HttpPost("{id}/vote")]
    [EnableRateLimiting("vote-writes")]
    public async Task<ActionResult<VoteResultDto>> Vote(Guid id, VoteRequest request)
    {
        var result = await _voteService.VoteAsync(id, request);
        await _hubContext.Clients.All.SendAsync("ReportUpdated", result);

        if (result.Status == ReportStatus.Resolved)
            await _hubContext.Clients.All.SendAsync("RemoveReport", result.FloodReportId);

        return Ok(result);
    }
}