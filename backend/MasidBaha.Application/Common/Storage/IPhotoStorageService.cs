namespace MasidBaha.Application.Common.Storage;

/// <summary>
/// Abstraction over where processed report photos actually end up (local disk or S3-compatible,
/// chosen by Storage:Provider). Callers get back either a relative path (local) or an absolute URL (S3);
/// IPhotoUrlPolicy turns that into the public URL.
/// </summary>
public interface IPhotoStorageService
{
    /// <summary>
    /// Persists the given bytes under a storage-provider-specific location and
    /// returns a relative path (e.g. "/uploads/2026/07/abc123.jpg") that can be
    /// combined with a base URL, or requested directly if served as static files.
    /// </summary>
    Task<string> SaveAsync(byte[] content, string fileName, string contentType, CancellationToken cancellationToken = default);
}
