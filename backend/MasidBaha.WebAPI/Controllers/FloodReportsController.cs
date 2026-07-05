using MasidBaha.Application.FloodReports.CreateReport;
using Microsoft.AspNetCore.Mvc;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[Route("api/flood-reports")]
public class FloodReportsController : ControllerBase
{
    private readonly ICreateFloodReportService _createService;

    public FloodReportsController(ICreateFloodReportService createService)
    {
        _createService = createService;
    }

    [HttpPost]
    public async Task<ActionResult<FloodReportDto>> Create(CreateFloodReportRequest request)
    {
        var report = await _createService.CreateAsync(request);
        return Ok(report);
    }
}