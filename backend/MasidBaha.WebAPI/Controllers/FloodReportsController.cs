using Asp.Versioning;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Exceptions;
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
    private readonly IRuntimeSettings _settings;
    private readonly IBanService _bans;
    private readonly IIpHasher _ipHasher;
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
        IRuntimeSettings settings,
        IBanService bans,
        IIpHasher ipHasher,
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
        _settings = settings;
        _bans = bans;
        _ipHasher = ipHasher;
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
        request.ReporterIpHash = _ipHasher.Hash(HttpContext);

        var verdict = await _bans.CheckAsync(request.ReporterSessionId, request.ReporterIpHash, cancellationToken);
        if (verdict == BanVerdict.Block)
            throw new ForbiddenException("Reporting is not available from this device.");

        // A shadow-banned source gets the same success response as anyone else, but
        // the report is saved hidden and nothing is shown, broadcast or pushed
        request.IsHidden = verdict == BanVerdict.Shadow;

        var created = await _createService.CreateOrGetAsync(request);
        var report = created.Report;

        // A retry of a report that was already saved gets the same answer, without telling everyone again
        if (request.IsHidden || created.WasAlreadySaved)
        {
            return CreatedAtAction(nameof(GetById), new { id = report.Id, version = "1" }, report);
        }

        await _cache.EvictByTagAsync(CacheTag, cancellationToken);
        await _hubContext.Clients.All.SendAsync("NewReport", report, cancellationToken);

        // Notifications go through a background queue, so the reporter isn't
        // kept waiting while every subscriber is pushed to. A Passable report
        // still shows on the map; it just doesn't buzz everyone's phone.
        if (report.Severity >= _settings.Current.PushMinimumSeverity)
        {
            var locationLabel = report.City ?? report.Province ?? "malapit sa iyo";
            await _pushQueue.EnqueueAsync(new PushPayload
            {
                Title = "Bagong ulat ng baha",
                Body = $"{report.Severity} sa {locationLabel}",
                Url = "/map",
                // Only people watching this spot (or who never chose places) are alerted
                Lat = report.Lat,
                Lng = report.Lng
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
        // Reading never issues a session cookie; a visitor without one is simply anyone
        return Ok(await _getByIdService.GetAsync(id, _session.TryGet(HttpContext)));
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

        var verdict = await _bans.CheckAsync(request.VoterSessionId, _ipHasher.Hash(HttpContext), cancellationToken);
        if (verdict == BanVerdict.Block)
            throw new ForbiddenException("Voting is not available from this device.");

        // A shadow-banned voter sees a normal answer, but nothing is recorded or broadcast
        var shadow = verdict == BanVerdict.Shadow;
        var result = await _voteService.VoteAsync(id, request, shadow);
        if (shadow)
            return Ok(result);

        await _cache.EvictByTagAsync(CacheTag, cancellationToken);
        await _hubContext.Clients.All.SendAsync("ReportUpdated", result, cancellationToken);

        if (result.Status == ReportStatus.Resolved)
            await _hubContext.Clients.All.SendAsync("RemoveReport", result.FloodReportId, cancellationToken);

        return Ok(result);
    }
}
