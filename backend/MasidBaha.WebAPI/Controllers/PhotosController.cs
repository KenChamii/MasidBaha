using Asp.Versioning;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.Photos.UploadPhoto;
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

    public PhotosController(IUploadPhotoService uploadPhotoService, IPhotoUrlPolicy photoUrls)
    {
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
