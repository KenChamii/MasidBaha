namespace MasidBaha.WebAPI.Controllers;

public static class PhotoUrlValidator
{
    // A report may only reference a photo this API itself produced (see
    // PhotosController.Upload): same host as the request, under /uploads/.
    // Anything else would let a client store arbitrary third-party URLs.
    public static bool IsOwnUpload(string? photoUrl, string requestHost)
    {
        if (string.IsNullOrEmpty(photoUrl)) return true;

        return Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.Equals(uri.Host, requestHost, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/uploads/", StringComparison.Ordinal);
    }
}
