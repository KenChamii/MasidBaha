using MasidBaha.Application.Common.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MasidBaha.Application.Photos.UploadPhoto;

public interface IUploadPhotoService
{
    /// <summary>
    /// Validates, resizes, compresses, and stores an uploaded report photo.
    /// Returns the relative path from the storage provider — the caller
    /// (controller) turns this into an absolute public URL.
    /// </summary>
    Task<string> UploadAsync(Stream content, string contentType, long lengthBytes, CancellationToken cancellationToken = default);
}

public class UploadPhotoService : IUploadPhotoService
{
    // Anything bigger is rejected outright before we even try to decode it —
    // no point spending CPU decompressing a 200MB "photo".
    private const long MaxUploadBytes = 8 * 1024 * 1024; // 8 MB
    private const int MaxLongestSidePixels = 1600;
    private const int JpegQuality = 78;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private readonly IPhotoStorageService _storageService;

    public UploadPhotoService(IPhotoStorageService storageService)
    {
        _storageService = storageService;
    }

    public async Task<string> UploadAsync(Stream content, string contentType, long lengthBytes, CancellationToken cancellationToken = default)
    {
        if (lengthBytes <= 0)
            throw new ArgumentException("Walang laman ang na-upload na file.");

        if (lengthBytes > MaxUploadBytes)
            throw new ArgumentException($"Ang photo ay dapat hindi lalagpas sa {MaxUploadBytes / (1024 * 1024)}MB.");

        if (!AllowedContentTypes.Contains(contentType))
            throw new ArgumentException("Tanging JPEG, PNG, o WebP na larawan lang ang tinatanggap.");

        Image image;
        try
        {
            // Decoding is also our real content-type check — a renamed .exe with a
            // "image/jpeg" header on it will fail here regardless of what the
            // browser claimed the content-type was.
            image = await Image.LoadAsync(content, cancellationToken);
        }
        catch (UnknownImageFormatException)
        {
            throw new ArgumentException("Hindi wastong larawan ang na-upload na file.");
        }

        using (image)
        {
            // Downscale only — never upscale a smaller photo.
            if (image.Width > MaxLongestSidePixels || image.Height > MaxLongestSidePixels)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxLongestSidePixels, MaxLongestSidePixels)
                }));
            }

            // Strip EXIF/IPTC/XMP — a flood photo can carry embedded GPS coordinates
            // and device info in its metadata that the reporter didn't intend to share
            // (their exact location is already captured separately, deliberately, via
            // the map pin — this is metadata leaking behind their back).
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            using var outputStream = new MemoryStream();
            await image.SaveAsync(outputStream, new JpegEncoder { Quality = JpegQuality }, cancellationToken);

            var fileName = $"{Guid.NewGuid():N}.jpg";
            return await _storageService.SaveAsync(outputStream.ToArray(), fileName, "image/jpeg", cancellationToken);
        }
    }
}
