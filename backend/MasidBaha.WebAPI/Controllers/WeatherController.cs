using Asp.Versioning;
using MasidBaha.Application.Weather;
using Microsoft.AspNetCore.Mvc;

namespace MasidBaha.WebAPI.Controllers;

// Weather context for the map. Public and read-only; the data is cached on the server, so this is
// cheap however many visitors ask.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/weather")]
public class WeatherController : ControllerBase
{
    private readonly ITyphoonService _typhoons;

    public WeatherController(ITyphoonService typhoons) => _typhoons = typhoons;

    /// <summary>Tropical cyclones near the Philippines: position, track, forecast cone and wind areas.</summary>
    [HttpGet("typhoons")]
    [ProducesResponseType<TyphoonsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<TyphoonsDto> GetTyphoons()
    {
        var current = _typhoons.Current;
        if (current is null)
        {
            // The first download after a start takes about half a minute; ask again shortly
            Response.Headers.RetryAfter = "30";
            return Problem(
                title: "Typhoon data is loading",
                detail: "The weather data is still being fetched. Try again in a minute.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Browsers and the edge may keep it for a few minutes: storms move slowly
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(current);
    }
}
