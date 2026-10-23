using Asp.Versioning;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.Photos.UploadPhoto;
using MasidBaha.WebAPI.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/photos")]
public class PhotosController : ControllerBase
{
    private readonly IUploadPhotoService _uploadPhotoService;
    private readonly IPhotoUrlPolicy _photoUrls;
    private readonly IBanService _bans;
    private readonly ISessionIdentity _session;
    private readonly IIpHasher _ipHasher;

    public PhotosController(
        IUploadPhotoService uploadPhotoService,
        IPhotoUrlPolicy photoUrls,
        IBanService bans,
        ISessionIdentity session,
        IIpHasher ipHasher)
    {
        _bans = bans;
        _session = session;
        _ipHasher = ipHasher;
        _uploadPhotoService = uploadPhotoService;
        _photoUrls = photoUrls;
    }

    // Separate endpoint from POST /flood-reports on purpose: the frontend
    // uploads the photo first, gets a URL back, then sends that URL as part of
    // the normal (still-JSON) CreateFloodReportRequest, so that request/response
    // contract doesn't change.
    /// <summary>Uploads a report photo (JPEG, PNG or WebP). Metadata is stripped and the image resized.</summary>
    [HttpPost]
    [EnableRateLimiting("report-writes")]
    [RequestSizeLimit(10 * 1024 * 1024)] // slightly above the service's own limit, to allow for multipart overhead
    [ProducesResponseType<PhotoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PhotoDto>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        // Blocked sources can't upload either (a shadow ban only affects what others see)
        var verdict = await _bans.CheckAsync(_session.GetOrCreate(HttpContext), _ipHasher.Hash(HttpContext), cancellationToken);
        if (verdict == BanVerdict.Block)
            throw new ForbiddenException("Uploading is not available from this device.");

        if (file is null || file.Length == 0)
            return Problem(detail: "Walang natanggap na file.", statusCode: StatusCodes.Status400BadRequest);

        string storedPath;
        try
        {
            await using var stream = file.OpenReadStream();
            storedPath = await _uploadPhotoService.UploadAsync(stream, file.ContentType, file.Length, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var publicUrl = _photoUrls.ToPublicUrl(storedPath, $"{Request.Scheme}://{Request.Host}");
        return Ok(new PhotoDto { Url = publicUrl });
    }
}
