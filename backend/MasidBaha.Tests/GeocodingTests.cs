using System.Net;
using System.Text;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasidBaha.Tests;

public class GeocodingTests
{
    private const string MarikinaJson =
        """{"address":{"state":"Metro Manila","city":"Marikina","region":"National Capital Region"}}""";

    private class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int Calls;
        public List<DateTime> CallTimes { get; } = new();

        public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            lock (CallTimes) CallTimes.Add(DateTime.UtcNow);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static NominatimGeocodingService Create(CountingHandler handler, GeocodingRateGate? gate = null, int minIntervalMs = 0)
    {
        var options = Options.Create(new GeocodingOptions { MinIntervalMilliseconds = minIntervalMs });
        return new NominatimGeocodingService(
            new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.test/") },
            new MemoryCache(new MemoryCacheOptions()),
            gate ?? new GeocodingRateGate(),
            options,
            NullLogger<NominatimGeocodingService>.Instance);
    }

    [Fact]
    public async Task ParsesRegionProvinceAndCity()
    {
        var geocoder = Create(new CountingHandler(_ => Json(MarikinaJson)));

        var result = await geocoder.ReverseGeocodeAsync(14.6507, 121.1029);

        Assert.Equal("National Capital Region", result.Region);
        Assert.Equal("Metro Manila", result.Province);
        Assert.Equal("Marikina", result.City);
    }

    [Fact]
    public async Task NearbyCoordinates_ShareOneCachedLookup()
    {
        var handler = new CountingHandler(_ => Json(MarikinaJson));
        var geocoder = Create(handler);

        await geocoder.ReverseGeocodeAsync(14.65071, 121.10291);
        var second = await geocoder.ReverseGeocodeAsync(14.65074, 121.10294); // ~0.5 m away: same rounded cell

        Assert.Equal(1, handler.Calls);
        Assert.Equal("Marikina", second.City);
    }

    [Fact]
    public async Task DistantCoordinates_AreLookedUpSeparately()
    {
        var handler = new CountingHandler(_ => Json(MarikinaJson));
        var geocoder = Create(handler);

        await geocoder.ReverseGeocodeAsync(14.6507, 121.1029);
        await geocoder.ReverseGeocodeAsync(10.3157, 123.8854);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Failures_ReturnAnEmptyResult_AndAreNotCached()
    {
        var fail = true;
        var handler = new CountingHandler(_ => fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(MarikinaJson));
        var geocoder = Create(handler);

        var failed = await geocoder.ReverseGeocodeAsync(14.6507, 121.1029);
        Assert.Null(failed.City); // the report can still be saved without an address

        fail = false;
        var recovered = await geocoder.ReverseGeocodeAsync(14.6507, 121.1029);

        Assert.Equal("Marikina", recovered.City); // the outage wasn't remembered
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task OutboundCalls_AreSpacedByTheMinimumInterval()
    {
        var handler = new CountingHandler(_ => Json(MarikinaJson));
        var geocoder = Create(handler, minIntervalMs: 250);

        // Three different places, requested at the same moment
        await Task.WhenAll(
            geocoder.ReverseGeocodeAsync(14.0, 121.0),
            geocoder.ReverseGeocodeAsync(10.0, 123.0),
            geocoder.ReverseGeocodeAsync(7.0, 125.0));

        Assert.Equal(3, handler.CallTimes.Count);
        var ordered = handler.CallTimes.OrderBy(t => t).ToList();
        Assert.All(new[] { ordered[1] - ordered[0], ordered[2] - ordered[1] },
            gap => Assert.True(gap >= TimeSpan.FromMilliseconds(200), $"calls were only {gap.TotalMilliseconds:F0} ms apart"));
    }
}
