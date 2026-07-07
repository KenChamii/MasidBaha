namespace MasidBaha.Application.Common.Storage;

/// <summary>
/// Dev/local implementation — writes under wwwroot/uploads so ASP.NET Core's static
/// file middleware can serve it back out. Files are bucketed by year/month so a single
/// folder doesn't end up with tens of thousands of entries.
/// Swap for AzureBlobPhotoStorageService (same interface) when ready to go cloud.
///
/// Takes the already-resolved absolute root path rather than figuring it out itself —
/// the WebAPI layer is the one that actually knows the content root / wwwroot location
/// (AppContext.BaseDirectory points at the bin/ output folder, not the content root,
/// so resolving it from inside this Application-layer class would silently pick the
/// wrong directory in Development).
/// </summary>
public class LocalDiskPhotoStorageService : IPhotoStorageService
{
    private readonly string _rootPath;

    public LocalDiskPhotoStorageService(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async Task<string> SaveAsync(byte[] content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var bucket = DateTime.UtcNow.ToString("yyyy/MM");
        var directory = Path.Combine(_rootPath, bucket);
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);

        // Forward slashes regardless of OS — this becomes part of a URL.
        return $"/uploads/{bucket}/{fileName}".Replace('\\', '/');
    }
}
