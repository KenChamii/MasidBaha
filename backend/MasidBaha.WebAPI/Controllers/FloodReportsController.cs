using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using Microsoft.AspNetCore.Mvc;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[Route("api/flood-reports")]
public class FloodReportsController : ControllerBase
{
    private readonly ICreateFloodReportService _createService;
    private readonly IGetNearbyReportsService _nearbyService;

    public FloodReportsController(
        ICreateFloodReportService createService,
        IGetNearbyReportsService nearbyService)
    {
        _createService = createService;
        _nearbyService = nearbyService;
    }

    [HttpPost]
    public async Task<ActionResult<FloodReportDto>> Create(CreateFloodReportRequest request)
    {
        var report = await _createService.CreateAsync(request);
        return Ok(report);
    }

    [HttpGet]
    public async Task<ActionResult<List<FloodReportDto>>> GetNearby([FromQuery] NearbyReportsQuery query)
    {
        var reports = await _nearbyService.GetNearbyAsync(query);
        return Ok(reports);
    }
}