using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[Route("api/flood-reports")]
public class FloodReportsController : ControllerBase
{
    private readonly ICreateFloodReportService _createService;
    private readonly IGetNearbyReportsService _nearbyService;
    private readonly IHubContext<FloodHub> _hubContext;

    public FloodReportsController(
        ICreateFloodReportService createService,
        IGetNearbyReportsService nearbyService,
        IHubContext<FloodHub> hubContext)
    {
        _createService = createService;
        _nearbyService = nearbyService;
        _hubContext = hubContext;
    }

    [HttpPost]
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
}