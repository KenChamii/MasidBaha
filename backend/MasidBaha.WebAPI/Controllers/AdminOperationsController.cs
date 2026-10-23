using Asp.Versioning;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Admin.Broadcasts;
using MasidBaha.Application.Admin.Dashboard;
using MasidBaha.Application.Admin.Moderation;
using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Common;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.Controllers;

// Dashboard, moderation queue, bans, broadcasts, audit log and runtime settings.
// Reading needs any staff role; acting needs Moderator; audit and settings are SuperAdmin only.
[ApiController]
[ApiVersion("1.0")]
[Authorize(Policy = AdminPolicies.CanView)]
[Route("api/v{version:apiVersion}/admin")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class AdminOperationsController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IModerationService _moderation;
    private readonly IBanService _bans;
    private readonly IBroadcastService _broadcasts;
    private readonly IAuditLog _audit;
    private readonly IRuntimeSettings _settings;
    private readonly IOutputCacheStore _cache;
    private readonly IHubContext<FloodHub> _hub;

    public AdminOperationsController(
        IDashboardService dashboard,
        IModerationService moderation,
        IBanService bans,
        IBroadcastService broadcasts,
        IAuditLog audit,
        IRuntimeSettings settings,
        IOutputCacheStore cache,
        IHubContext<FloodHub> hub)
    {
        _dashboard = dashboard;
        _moderation = moderation;
        _bans = bans;
        _broadcasts = broadcasts;
        _audit = audit;
        _settings = settings;
        _cache = cache;
        _hub = hub;
    }

    // ---- dashboard ----

    /// <summary>Headline numbers and 30-day series for the admin home page.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken cancellationToken) =>
        Ok(await _dashboard.GetAsync(cancellationToken));

    // ---- moderation ----

    /// <summary>Active reports that look suspicious, with the reasons they were flagged.</summary>
    [HttpGet("moderation/queue")]
    [ProducesResponseType<PagedResult<ModerationQueueItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ModerationQueueItemDto>>> Queue([FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        Ok(await _moderation.GetQueueAsync(query, cancellationToken));

    /// <summary>Applies one action (resolve, expire, hide, delete...) to many reports at once.</summary>
    [HttpPost("moderation/bulk")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType<BulkActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkActionResult>> Bulk(BulkReportActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _moderation.ApplyBulkAsync(request, cancellationToken);
        await _cache.EvictByTagAsync(FloodReportsController.CacheTag, cancellationToken);

        // Everything but "bring back" takes the report off live maps
        if (request.Action is not (BulkAction.Reactivate or BulkAction.Unhide))
        {
            foreach (var id in result.AffectedIds)
                await _hub.Clients.All.SendAsync("RemoveReport", id, cancellationToken);
        }

        return Ok(result);
    }

    // ---- bans ----

    /// <summary>Bans, newest first.</summary>
    [HttpGet("bans")]
    [ProducesResponseType<PagedResult<BanDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BanDto>>> Bans([FromQuery] BanQuery query, CancellationToken cancellationToken) =>
        Ok(await _bans.ListAsync(query, cancellationToken));

    /// <summary>Bans a reporting session or source IP: Block rejects it, Shadow quietly hides what it posts.</summary>
    [HttpPost("bans")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType<BanResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BanResult>> CreateBan(CreateBanRequest request, CancellationToken cancellationToken)
    {
        var result = await _bans.CreateAsync(request, cancellationToken);

        if (result.HiddenReportIds.Count > 0)
        {
            await _cache.EvictByTagAsync(FloodReportsController.CacheTag, cancellationToken);
            foreach (var id in result.HiddenReportIds)
                await _hub.Clients.All.SendAsync("RemoveReport", id, cancellationToken);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Lifts a ban straight away.</summary>
    [HttpDelete("bans/{id:guid}")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType<BanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BanDto>> LiftBan(Guid id, CancellationToken cancellationToken) =>
        Ok(await _bans.LiftAsync(id, cancellationToken));

    // ---- broadcasts ----

    /// <summary>Past announcements.</summary>
    [HttpGet("broadcasts")]
    [ProducesResponseType<PagedResult<BroadcastDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BroadcastDto>>> Broadcasts([FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        Ok(await _broadcasts.ListAsync(query, cancellationToken));

    /// <summary>Sends a push notification to every subscriber.</summary>
    [HttpPost("broadcasts")]
    [Authorize(Policy = AdminPolicies.CanModerate)]
    [ProducesResponseType<BroadcastDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BroadcastDto>> SendBroadcast(SendBroadcastRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await _broadcasts.SendAsync(request, cancellationToken));

    // ---- audit ----

    /// <summary>Who did what, newest first.</summary>
    [HttpGet("audit")]
    [Authorize(Policy = AdminPolicies.ManageUsers)]
    [ProducesResponseType<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditEntryDto>>> Audit([FromQuery] AuditQuery query, CancellationToken cancellationToken) =>
        Ok(await _audit.QueryAsync(query, cancellationToken));

    // ---- settings ----

    /// <summary>Current runtime settings next to their configured defaults.</summary>
    [HttpGet("settings")]
    [Authorize(Policy = AdminPolicies.ManageUsers)]
    [ProducesResponseType<SettingsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsView>> GetSettings(CancellationToken cancellationToken) =>
        Ok(await _settings.GetViewAsync(cancellationToken));

    /// <summary>Overrides settings without a redeploy. Other API instances pick it up within a minute.</summary>
    [HttpPut("settings")]
    [Authorize(Policy = AdminPolicies.ManageUsers)]
    [ProducesResponseType<SettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SettingsView>> UpdateSettings(UpdateSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await _settings.UpdateAsync(request, cancellationToken));

    /// <summary>Drops every override and goes back to the configured defaults.</summary>
    [HttpDelete("settings")]
    [Authorize(Policy = AdminPolicies.ManageUsers)]
    [ProducesResponseType<SettingsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsView>> ResetSettings(CancellationToken cancellationToken) =>
        Ok(await _settings.ResetAsync(cancellationToken));
}
