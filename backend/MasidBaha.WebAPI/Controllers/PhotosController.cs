using MasidBaha.Application.Photos.UploadPhoto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[Route("api/photos")]
public class PhotosController : ControllerBase
{
    private readonly IUploadPhotoService _uploadPhotoService;

    public PhotosController(IUploadPhotoService uploadPhotoService)
    {
        _uploadPhotoService = uploadPhotoService;
    }

    // Separate endpoint from POST /api/flood-reports on purpose: the frontend
    // uploads the photo first, gets a URL back, then sends that URL as part of
    // the normal (still-JSON) CreateFloodReportRequest — no need to touch that
    // request/response contract at all.
    [HttpPost]
    [EnableRateLimiting("report-writes")]
    [RequestSizeLimit(10 * 1024 * 1024)] // slightly above the service's own 8MB check, to allow for multipart overhead
    public async Task<ActionResult<PhotoDto>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Walang natanggap na file." });

        string relativePath;
        try
        {
            await using var stream = file.OpenReadStream();
            relativePath = await _uploadPhotoService.UploadAsync(stream, file.ContentType, file.Length, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var publicUrl = $"{Request.Scheme}://{Request.Host}{relativePath}";
        return Ok(new PhotoDto { Url = publicUrl });
    }
}
