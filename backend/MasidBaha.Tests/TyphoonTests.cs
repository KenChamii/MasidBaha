using System.Text.Json;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasidBaha.Tests;

public static class TyphoonFixture
{
    // Shaped like GDACS's GeoJSON: GeoJSON order is [lng, lat]. Two storms near the Philippines
    // (one Red, one Orange, listed Orange first), one far away in the south Pacific, and two broken features.
    public const string Json = """
    {
      "type": "FeatureCollection",
      "features": [
        { "type": "Feature", "geometry": { "type": "Point", "coordinates": [125.0, 14.0] },
          "properties": { "eventid": 2, "eventname": "MIDDLE-26", "alertlevel": "Orange", "Class": "Point_Centroid", "polygonlabel": "Centroid", "source": "JTWC" } },

        { "type": "Feature", "geometry": { "type": "Point", "coordinates": [123.8049, 22.7051] },
          "properties": { "eventid": 1, "eventname": "TESTO-26", "alertlevel": "Red", "Class": "Point_Centroid", "polygonlabel": "Centroid",
                         "source": "JTWC", "country": "Philippines", "url": { "report": "https://www.gdacs.org/report.aspx?eventid=1" } } },

        { "type": "Feature", "geometry": { "type": "LineString", "coordinates": [[123.8, 22.7], [124.5, 24.0]] },
          "properties": { "eventid": 1, "Class": "Line_Line_1", "polygonlabel": "HU" } },
        { "type": "Feature", "geometry": { "type": "LineString", "coordinates": [[122.0, 20.0], [123.8, 22.7]] },
          "properties": { "eventid": 1, "Class": "Line_Line_0", "polygonlabel": "TS" } },

        { "type": "Feature", "geometry": { "type": "Polygon", "coordinates": [[[123.0, 22.0], [125.0, 22.0], [125.0, 25.0], [123.0, 25.0], [123.0, 22.0]]] },
          "properties": { "eventid": 1, "Class": "Poly_Cones", "polygonlabel": "Uncertainty Cones" } },

        { "type": "Feature", "geometry": { "type": "Polygon", "coordinates": [[[122.5, 21.5], [125.5, 21.5], [125.5, 24.0], [122.5, 24.0], [122.5, 21.5]]] },
          "properties": { "eventid": 1, "Class": "Poly_Orange", "polygonlabel": "90 km/h" } },

        { "type": "Feature", "geometry": { "type": "Polygon", "coordinates": [[[123.9, 22.9], [124.1, 22.9], [124.1, 23.1], [123.9, 23.1], [123.9, 22.9]]] },
          "properties": { "eventid": 1, "Class": "Point_Polygon_Point_0", "polygonlabel": "24/10 06:00 UTC" } },

        { "type": "Feature", "geometry": { "type": "Point", "coordinates": [-156.8, -20.7] },
          "properties": { "eventid": 3, "eventname": "FARAWAY-26", "alertlevel": "Green", "Class": "Point_Centroid" } },
        { "type": "Feature", "geometry": { "type": "LineString", "coordinates": [[-156.8, -20.7], [-155.0, -22.0]] },
          "properties": { "eventid": 3, "Class": "Line_Line_0", "polygonlabel": "TS" } },

        { "type": "Feature" },
        { "type": "Feature", "geometry": { "type": "LineString", "coordinates": "not coordinates" },
          "properties": { "eventid": 1, "Class": "Line_Line_9", "polygonlabel": "TS" } }
      ]
    }
    """;
}

public class TyphoonParserTests
{
    private static readonly WeatherOptions Region = new();

    [Fact]
    public void KeepsOnlyStormsNearThePhilippines_MostDangerousFirst()
    {
        var storms = TyphoonParser.Parse(TyphoonFixture.Json, Region);

        Assert.Equal(["TESTO-26", "MIDDLE-26"], storms.Select(s => s.Name));
        Assert.DoesNotContain(storms, s => s.Name == "FARAWAY-26");
    }

    [Fact]
    public void MapsTheStormCentre_FromGeoJsonOrder_AndRoundsIt()
    {
        var storm = TyphoonParser.Parse(TyphoonFixture.Json, Region)[0];

        Assert.Equal(1, storm.Id);
        Assert.Equal("Red", storm.AlertLevel);
        Assert.Equal("JTWC", storm.Source);
        Assert.Equal("Philippines", storm.Country);
        Assert.Equal("https://www.gdacs.org/report.aspx?eventid=1", storm.ReportUrl);
        Assert.Equal(22.71, storm.Lat); // GeoJSON is [lng, lat]
        Assert.Equal(123.80, storm.Lng);
    }

    [Fact]
    public void TrackSegments_AreInTrackOrder_WithTheirCategory_AndLatLngPairs()
    {
        var track = TyphoonParser.Parse(TyphoonFixture.Json, Region)[0].Track;

        // Line_Line_0 comes before Line_Line_1 however the source listed them; the broken Line_Line_9 is skipped
        Assert.Equal(["TS", "HU"], track.Select(t => t.Category));
        Assert.Equal([20.0, 122.0], track[0].Points[0]);
    }

    [Fact]
    public void ReadsTheConeWindAreasAndForecastPoints()
    {
        var storm = TyphoonParser.Parse(TyphoonFixture.Json, Region)[0];

        Assert.Single(storm.Cone);
        Assert.Equal(5, storm.Cone[0].Length);

        var wind = Assert.Single(storm.WindAreas);
        Assert.Equal(90, wind.KmPerHour);

        var forecast = Assert.Single(storm.ForecastPoints);
        Assert.Equal("24/10 06:00 UTC", forecast.Label);
        Assert.Equal(23.0, forecast.Lat);   // the middle of the little square, not skewed by its closing vertex
        Assert.Equal(124.0, forecast.Lng);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "features": [] }""")]
    [InlineData("""{ "features": "nope" }""")]
    public void AnythingWithoutFeatures_MeansNoStorms(string json)
    {
        Assert.Empty(TyphoonParser.Parse(json, Region));
    }

    [Fact]
    public void ABrokenDocument_Throws_SoTheCallerKeepsItsOldCopy()
    {
        Assert.ThrowsAny<JsonException>(() => TyphoonParser.Parse("<html>maintenance</html>", Region));
    }
}

public class TyphoonServiceTests
{
    private class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private class ScriptedSource : ITyphoonSource
    {
        public Func<string> Next { get; set; } = () => TyphoonFixture.Json;
        public Task<string> FetchAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next());
    }

    private static (TyphoonService Service, ScriptedSource Source, ManualTime Time) Create()
    {
        var source = new ScriptedSource();
        var time = new ManualTime();
        var provider = new ServiceCollection().AddSingleton<ITyphoonSource>(source).BuildServiceProvider();

        var service = new TyphoonService(
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new WeatherOptions()), time, NullLogger<TyphoonService>.Instance);
        return (service, source, time);
    }

    [Fact]
    public async Task NothingIsServed_UntilTheFirstRefreshHasFinished()
    {
        var (service, _, _) = Create();

        Assert.Null(service.Current);

        await service.RefreshAsync();

        var current = service.Current!;
        Assert.Equal(2, current.Storms.Count);
        Assert.False(current.Stale);
    }

    [Fact]
    public async Task AFailedRefresh_KeepsThePreviousCopy()
    {
        var (service, source, _) = Create();
        await service.RefreshAsync();

        source.Next = () => throw new HttpRequestException("GDACS is down");
        await Assert.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync());

        Assert.Equal(2, service.Current!.Storms.Count);
    }

    [Fact]
    public async Task OldData_IsMarkedStale_AndThenDropped()
    {
        var (service, _, time) = Create();
        await service.RefreshAsync();

        time.Now += TimeSpan.FromMinutes(20);   // within two refresh periods
        Assert.False(service.Current!.Stale);

        time.Now += TimeSpan.FromMinutes(20);   // two refreshes missed
        Assert.True(service.Current!.Stale);

        time.Now += TimeSpan.FromHours(7);      // older than anyone should trust
        Assert.Null(service.Current);
    }
}
