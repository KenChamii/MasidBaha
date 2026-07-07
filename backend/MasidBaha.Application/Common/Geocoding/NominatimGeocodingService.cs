using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MasidBaha.Application.Common.Geocoding;

// Reverse geocoding via OpenStreetMap Nominatim. Registered with a typed
// HttpClient in Program.cs (BaseAddress + UserAgent already configured there).
public class NominatimGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;

    public NominatimGeocodingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeocodeResult> ReverseGeocodeAsync(double lat, double lng)
    {
        try
        {
            var url = $"reverse?format=jsonv2&lat={lat}&lon={lng}&zoom=14&addressdetails=1";
            var response = await _httpClient.GetFromJsonAsync<NominatimReverseResponse>(url);

            var address = response?.Address;
            if (address is null)
                return new GeocodeResult();

            return new GeocodeResult
            {
                // Nominatim's PH address tagging is inconsistent — "region" isn't
                // always present, and "state" is the closest reliable match for
                // province in most PH results, so we fall back across a few keys.
                Region = address.Region ?? address.State,
                Province = address.State ?? address.StateDistrict ?? address.County,
                City = address.City ?? address.Town ?? address.Municipality ?? address.Village
            };
        }
        catch
        {
            // Geocoding is best-effort — a flood report should still save even if
            // Nominatim is down, rate-limiting us, or returns something unexpected.
            return new GeocodeResult();
        }
    }

    private class NominatimReverseResponse
    {
        [JsonPropertyName("address")]
        public NominatimAddress? Address { get; set; }
    }

    private class NominatimAddress
    {
        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("state_district")]
        public string? StateDistrict { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("town")]
        public string? Town { get; set; }

        [JsonPropertyName("municipality")]
        public string? Municipality { get; set; }

        [JsonPropertyName("village")]
        public string? Village { get; set; }
    }
}
