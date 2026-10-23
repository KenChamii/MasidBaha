using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Common.Geocoding;

// Spaces out outbound calls: Nominatim's usage policy allows at most one
// request per second. Shared (singleton) so every request goes through one gate.
public class GeocodingRateGate
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _lastCallUtc = DateTime.MinValue;

    public async Task WaitAsync(TimeSpan minInterval, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastCallUtc + minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            _lastCallUtc = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }
}

// Reverse geocoding via OpenStreetMap Nominatim. Registered with a typed
// HttpClient in Program.cs (base address, user agent and the resilience
// pipeline are configured there). Results are cached by rounded coordinates,
// so a busy neighbourhood doesn't hammer a free public service.
public class NominatimGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GeocodingRateGate _gate;
    private readonly GeocodingOptions _options;
    private readonly ILogger<NominatimGeocodingService> _logger;

    public NominatimGeocodingService(
        HttpClient httpClient,
        IMemoryCache cache,
        GeocodingRateGate gate,
        IOptions<GeocodingOptions> options,
        ILogger<NominatimGeocodingService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _gate = gate;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GeocodeResult> ReverseGeocodeAsync(double lat, double lng)
    {
        var key = $"geocode:{Math.Round(lat, _options.CacheDecimals)}:{Math.Round(lng, _options.CacheDecimals)}";
        if (_cache.TryGetValue(key, out GeocodeResult? cached) && cached is not null)
            return cached;

        try
        {
            await _gate.WaitAsync(TimeSpan.FromMilliseconds(_options.MinIntervalMilliseconds));

            var url = FormattableString.Invariant($"reverse?format=jsonv2&lat={lat}&lon={lng}&zoom=14&addressdetails=1");
            var response = await _httpClient.GetFromJsonAsync<NominatimReverseResponse>(url);
            var address = response?.Address;
            if (address is null)
                return new GeocodeResult();

            var result = new GeocodeResult
            {
                // Nominatim's PH address tagging is inconsistent: "region" isn't
                // always present, and "state" is the closest reliable match for
                // province in most PH results, so we fall back across a few keys.
                Region = address.Region ?? address.State,
                Province = address.State ?? address.StateDistrict ?? address.County,
                City = address.City ?? address.Town ?? address.Municipality ?? address.Village
            };

            // Only successful lookups are cached, so an outage doesn't stick
            _cache.Set(key, result, TimeSpan.FromMinutes(_options.CacheMinutes));
            return result;
        }
        catch (Exception ex)
        {
            // Geocoding is best-effort: a flood report should still save even if
            // Nominatim is down, rate-limiting us, or returns something unexpected.
            _logger.LogWarning(ex, "Reverse geocoding failed for {Lat},{Lng}", lat, lng);
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
