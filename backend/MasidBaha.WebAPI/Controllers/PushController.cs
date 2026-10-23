using Asp.Versioning;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.PushNotifications;
using MasidBaha.Application.PushNotifications.WatchAreas;
using MasidBaha.WebAPI.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/push")]
public class PushController : ControllerBase
{
    private readonly IPushSubscriptionService _subscriptionService;
    private readonly IWatchAreaService _watchAreas;
    private readonly ISessionIdentity _session;
    private readonly VapidOptions _vapid;

    public PushController(
        IPushSubscriptionService subscriptionService,
        IWatchAreaService watchAreas,
        ISessionIdentity session,
        IOptions<VapidOptions> vapid)
    {
        _subscriptionService = subscriptionService;
        _watchAreas = watchAreas;
        _session = session;
        _vapid = vapid.Value;
    }

    // The frontend fetches the VAPID public key at runtime instead of
    // hardcoding it in environment.ts, so rotating the key needs no rebuild.
    /// <summary>The public key the browser needs to subscribe to web push.</summary>
    [HttpGet("vapid-public-key")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<object> GetVapidPublicKey()
    {
        if (string.IsNullOrWhiteSpace(_vapid.PublicKey))
            return Problem(detail: "Push notifications are not configured on this server.", statusCode: StatusCodes.Status404NotFound);

        return Ok(new { publicKey = _vapid.PublicKey });
    }

    /// <summary>Registers (or refreshes) this browser's push subscription.</summary>
    [HttpPost("subscribe")]
    [EnableRateLimiting("push-writes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Subscribe(PushSubscriptionDto subscription)
    {
        subscription.SessionId = _session.GetOrCreate(HttpContext);

        await _subscriptionService.SubscribeAsync(subscription);
        return NoContent();
    }

    /// <summary>Removes a push subscription by its endpoint.</summary>
    [HttpPost("unsubscribe")]
    [EnableRateLimiting("push-writes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Unsubscribe(UnsubscribeRequest request)
    {
        await _subscriptionService.UnsubscribeAsync(request.Endpoint);
        return NoContent();
    }

    /// <summary>The places this visitor has chosen to be alerted about.</summary>
    [HttpGet("watch-areas")]
    [ProducesResponseType<List<WatchAreaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WatchAreaDto>>> GetWatchAreas(CancellationToken cancellationToken)
    {
        // No cookie yet means no areas, and a read must not hand out a session
        var sessionId = _session.TryGet(HttpContext);
        return Ok(sessionId is null ? new List<WatchAreaDto>() : await _watchAreas.ListAsync(sessionId, cancellationToken));
    }

    /// <summary>Adds a place to be alerted about (up to 5; needs alerts turned on first).</summary>
    [HttpPost("watch-areas")]
    [EnableRateLimiting("push-writes")]
    [ProducesResponseType<WatchAreaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WatchAreaDto>> AddWatchArea(CreateWatchAreaRequest request, CancellationToken cancellationToken)
    {
        var area = await _watchAreas.CreateAsync(_session.GetOrCreate(HttpContext), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, area);
    }

    /// <summary>Stops alerts for one of this visitor's places.</summary>
    [HttpDelete("watch-areas/{id:guid}")]
    [EnableRateLimiting("push-writes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveWatchArea(Guid id, CancellationToken cancellationToken)
    {
        var sessionId = _session.TryGet(HttpContext);
        if (sessionId is null || !await _watchAreas.DeleteAsync(sessionId, id, cancellationToken))
            return Problem(detail: "Watch area not found.", statusCode: StatusCodes.Status404NotFound);

        return NoContent();
    }
}
