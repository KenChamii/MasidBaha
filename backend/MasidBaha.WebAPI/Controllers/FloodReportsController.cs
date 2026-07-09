using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.GetHeatmapData;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.PushNotifications;
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
    private readonly IGetHeatmapDataService _heatmapService;
    private readonly IVoteOnReportService _voteService;
    private readonly IPushNotificationService _pushService;
    private readonly IHubContext<FloodHub> _hubContext;

    public FloodReportsController(
        ICreateFloodReportService createService,
        IGetNearbyReportsService nearbyService,
        IGetTopReportsService topService,
        IGetHeatmapDataService heatmapService,
        IVoteOnReportService voteService,
        IPushNotificationService pushService,
        IHubContext<FloodHub> hubContext)
    {
        _createService = createService;
        _nearbyService = nearbyService;
        _topService = topService;
        _heatmapService = heatmapService;
        _voteService = voteService;
        _pushService = pushService;
        _hubContext = hubContext;
    }

    [HttpPost]
    [EnableRateLimiting("report-writes")]
    public async Task<ActionResult<FloodReportDto>> Create(CreateFloodReportRequest request)
    {
        var report = await _createService.CreateAsync(request);
        await _hubContext.Clients.All.SendAsync("NewReport", report);

        // Push only fires for Tuhod (KneeLevel) severity and up. A Passable
        // report still shows on the map for anyone looking, it just won't
        // send a notification to everyone's phone.
        if (report.Severity >= Severity.KneeLevel)
        {
            var locationLabel = report.City ?? report.Province ?? "malapit sa iyo";
            await _pushService.BroadcastAsync(new PushPayload
            {
                Title = "Bagong ulat ng baha",
                Body = $"{report.Severity} sa {locationLabel}",
                Url = "/map"
            });
        }

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

    // Historical view for the analytics/heatmap page — deliberately separate
    // from GetNearby/GetTop since those are status-filtered (Active only)
    // and radius/scope-bound, while this spans all statuses and a date range.
    [HttpGet("heatmap")]
    public async Task<ActionResult<List<HeatmapPointDto>>> GetHeatmap([FromQuery] HeatmapQuery query)
    {
        var points = await _heatmapService.GetHeatmapAsync(query);
        return Ok(points);
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