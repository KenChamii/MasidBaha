namespace MasidBaha.Application.Common.Geocoding;

public interface IGeocodingService
{
    Task<GeocodeResult> ReverseGeocodeAsync(double lat, double lng);
}

public class GeocodeResult
{
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}
