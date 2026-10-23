using Asp.Versioning;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.PushNotifications;
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
    private readonly ISessionIdentity _session;
    private readonly VapidOptions _vapid;

    public PushController(IPushSubscriptionService subscriptionService, ISessionIdentity session, IOptions<VapidOptions> vapid)
    {
        _subscriptionService = subscriptionService;
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
}
