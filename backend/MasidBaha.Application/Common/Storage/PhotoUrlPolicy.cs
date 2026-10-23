using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Common.Storage;

// Knows which photo URLs this deployment itself can produce, so a report can't
// smuggle in an arbitrary third-party URL, and turns a stored path into a public URL.
public interface IPhotoUrlPolicy
{
    bool IsOwnUpload(string? photoUrl, string requestHost);
    string ToPublicUrl(string storedPath, string requestBaseUrl);
}

public class PhotoUrlPolicy : IPhotoUrlPolicy
{
    private readonly StorageOptions _options;

    public PhotoUrlPolicy(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    private bool UsesS3 => string.Equals(_options.Provider, "S3", StringComparison.OrdinalIgnoreCase);

    public bool IsOwnUpload(string? photoUrl, string requestHost)
    {
        if (string.IsNullOrEmpty(photoUrl)) return true;

        if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        if (UsesS3)
        {
            var publicBase = _options.S3.PublicBaseUrl;
            return !string.IsNullOrWhiteSpace(publicBase)
                && photoUrl.StartsWith(publicBase.TrimEnd('/') + "/uploads/", StringComparison.OrdinalIgnoreCase);
        }

        // Local disk: same host as the request, under /uploads/
        return string.Equals(uri.Host, requestHost, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/uploads/", StringComparison.Ordinal);
    }

    // S3 stores a full URL already; local storage returns a path such as /uploads/x.jpg. Only an
    // http(s) address counts as "already a full URL": on Linux a path starting with / also parses as an
    // absolute (file://) URI, which would otherwise leave the path unqualified.
    public string ToPublicUrl(string storedPath, string requestBaseUrl) =>
        Uri.TryCreate(storedPath, UriKind.Absolute, out var absolute) &&
        (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? storedPath
            : $"{requestBaseUrl.TrimEnd('/')}{storedPath}";
}
