using Asp.Versioning;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetHeatmapData;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetReport;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.PushNotifications;
using MasidBaha.WebAPI.Hubs;
using MasidBaha.WebAPI.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/flood-reports")]
public class FloodReportsController : ControllerBase
{
    // Tag shared by the cached read endpoints, evicted whenever reports change
    public const string CacheTag = "reports";

    private readonly ICreateFloodReportService _createService;
    private readonly IGetNearbyReportsService _nearbyService;
    private readonly IGetTopReportsService _topService;
    private readonly IGetHeatmapDataService _heatmapService;
    private readonly IGetReportByIdService _getByIdService;
    private readonly IVoteOnReportService _voteService;
    private readonly ISessionIdentity _session;
    private readonly IPhotoUrlPolicy _photoUrls;
    private readonly IPushQueue _pushQueue;
    private readonly PushOptions _pushOptions;
    private readonly IOutputCacheStore _cache;
    private readonly IHubContext<FloodHub> _hubContext;

    public FloodReportsController(
        ICreateFloodReportService createService,
        IGetNearbyReportsService nearbyService,
        IGetTopReportsService topService,
        IGetHeatmapDataService heatmapService,
        IGetReportByIdService getByIdService,
        IVoteOnReportService voteService,
        ISessionIdentity session,
        IPhotoUrlPolicy photoUrls,
        IPushQueue pushQueue,
        IOptions<PushOptions> pushOptions,
        IOutputCacheStore cache,
        IHubContext<FloodHub> hubContext)
    {
        _createService = createService;
        _nearbyService = nearbyService;
        _topService = topService;
        _heatmapService = heatmapService;
        _getByIdService = getByIdService;
        _voteService = voteService;
        _session = session;
        _photoUrls = photoUrls;
        _pushQueue = pushQueue;
        _pushOptions = pushOptions.Value;
        _cache = cache;
        _hubContext = hubContext;
    }

    /// <summary>Reports a flood at a location inside the Philippines.</summary>
    [HttpPost]
    [EnableRateLimiting("report-writes")]
    [ProducesResponseType<FloodReportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<FloodReportDto>> Create(CreateFloodReportRequest request, CancellationToken cancellationToken)
    {
        if (!_photoUrls.IsOwnUpload(request.PhotoUrl, Request.Host.Host))
        {
            ModelState.AddModelError(nameof(request.PhotoUrl), "PhotoUrl must point to a photo uploaded through this API.");
            return ValidationProblem(ModelState);
        }

        // The reporter is whoever holds the signed session cookie, never a client-supplied id
        request.ReporterSessionId = _session.GetOrCreate(HttpContext);

        var report = await _createService.CreateAsync(request);

        await _cache.EvictByTagAsync(CacheTag, cancellationToken);
        await _hubContext.Clients.All.SendAsync("NewReport", report, cancellationToken);

        // Notifications go through a background queue, so the reporter isn't
        // kept waiting while every subscriber is pushed to. A Passable report
        // still shows on the map; it just doesn't buzz everyone's phone.
        if (report.Severity >= _pushOptions.MinimumSeverity)
        {
            var locationLabel = report.City ?? report.Province ?? "malapit sa iyo";
            await _pushQueue.EnqueueAsync(new PushPayload
            {
                Title = "Bagong ulat ng baha",
                Body = $"{report.Severity} sa {locationLabel}",
                Url = "/map"
            });
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = report.Id, version = "1" },
            report);
    }

    /// <summary>Gets a single report by id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<FloodReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FloodReportDto>> GetById(Guid id)
    {
        return Ok(await _getByIdService.GetAsync(id));
    }

    /// <summary>Active reports within a radius (metres) of a point.</summary>
    [HttpGet]
    [ProducesResponseType<List<FloodReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<FloodReportDto>>> GetNearby([FromQuery] NearbyReportsQuery query)
    {
        return Ok(await _nearbyService.GetNearbyAsync(query));
    }

    /// <summary>Most-confirmed active reports, optionally scoped to a region, province or city.</summary>
    [HttpGet("top")]
    [OutputCache(PolicyName = "top-reports")]
    [ProducesResponseType<List<FloodReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<FloodReportDto>>> GetTop([FromQuery] TopReportsQuery query)
    {
        return Ok(await _topService.GetTopAsync(query));
    }

    // Historical view for the analytics/heatmap page. Deliberately separate
    // from GetNearby/GetTop: those are status-filtered (Active only) and
    // radius/scope-bound, while this spans all statuses and a date range.
    /// <summary>Historical reports of every status, for the flood-prone-areas heatmap.</summary>
    [HttpGet("heatmap")]
    [OutputCache(PolicyName = "heatmap")]
    [ProducesResponseType<List<HeatmapPointDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<HeatmapPointDto>>> GetHeatmap([FromQuery] HeatmapQuery query)
    {
        return Ok(await _heatmapService.GetHeatmapAsync(query));
    }

    /// <summary>Confirms (1) or denies (-1) a report. One vote per session per report.</summary>
    [HttpPost("{id:guid}/vote")]
    [EnableRateLimiting("vote-writes")]
    [ProducesResponseType<VoteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<VoteResultDto>> Vote(Guid id, VoteRequest request, CancellationToken cancellationToken)
    {
        request.VoterSessionId = _session.GetOrCreate(HttpContext);

        var result = await _voteService.VoteAsync(id, request);

        await _cache.EvictByTagAsync(CacheTag, cancellationToken);
        await _hubContext.Clients.All.SendAsync("ReportUpdated", result, cancellationToken);

        if (result.Status == ReportStatus.Resolved)
            await _hubContext.Clients.All.SendAsync("RemoveReport", result.FloodReportId, cancellationToken);

        return Ok(result);
    }
}
