using System.Globalization;
using System.Text.Json;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Weather;

// Tropical cyclones near the Philippines, trimmed for the map. The data comes from GDACS (the UN / EU
// Global Disaster Alert and Coordination System), which republishes track forecasts from the Joint
// Typhoon Warning Center. PAGASA has no public map-data service, so the app links to its official
// bulletins next to this overlay instead of pretending to be it.

public class TyphoonsDto
{
    public DateTime GeneratedAt { get; set; }

    // True when the source could not be reached and this is the last good copy
    public bool Stale { get; set; }

    public List<TyphoonDto> Storms { get; set; } = new();
}

public class TyphoonDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Green, Orange or Red: GDACS's own alert level for the storm
    public string AlertLevel { get; set; } = "Green";

    public string? Source { get; set; }
    public string? Country { get; set; }
    public string? ReportUrl { get; set; }

    // Where the storm centre is now
    public double Lat { get; set; }
    public double Lng { get; set; }

    public List<TrackSegmentDto> Track { get; set; } = new();

    // The area the centre may pass through (the "cone of uncertainty"), as rings of [lat, lng]
    public List<double[][]> Cone { get; set; } = new();

    // Where winds of each strength reach right now
    public List<WindAreaDto> WindAreas { get; set; } = new();

    public List<ForecastPointDto> ForecastPoints { get; set; } = new();
}

public class TrackSegmentDto
{
    // TD (depression), TS (tropical storm), HU (typhoon-strength), as given by the source
    public string Category { get; set; } = string.Empty;

    // [lat, lng] pairs
    public double[][] Points { get; set; } = [];
}

public class WindAreaDto
{
    public int KmPerHour { get; set; }
    public double[][] Ring { get; set; } = [];
}

public class ForecastPointDto
{
    // When the storm is expected there, e.g. "24/10 06:00 UTC"
    public string Label { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
}

// Reads GDACS's GeoJSON. Pure and forgiving: a feature it cannot read is skipped, never fatal.
public static class TyphoonParser
{
    public static List<TyphoonDto> Parse(string geoJson, WeatherOptions region)
    {
        using var document = JsonDocument.Parse(geoJson);
        if (!document.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return [];

        var storms = new Dictionary<int, TyphoonDto>();
        var linesByStorm = new Dictionary<int, List<(int Order, TrackSegmentDto Segment)>>();

        // Pass 1: the storm centres decide which storms matter here at all
        foreach (var feature in features.EnumerateArray())
        {
            if (Class(feature) != "Point_Centroid" || !TryPoint(feature, out var lat, out var lng)) continue;
            if (lat < region.MinLat || lat > region.MaxLat || lng < region.MinLng || lng > region.MaxLng) continue;

            var props = feature.GetProperty("properties");
            if (!TryInt(props, "eventid", out var id)) continue;

            storms[id] = new TyphoonDto
            {
                Id = id,
                Name = Text(props, "eventname") ?? "Unnamed",
                AlertLevel = Text(props, "alertlevel") ?? "Green",
                Source = Text(props, "source"),
                Country = Text(props, "country"),
                ReportUrl = props.TryGetProperty("url", out var url) ? Text(url, "report") : null,
                Lat = Round(lat),
                Lng = Round(lng)
            };
            linesByStorm[id] = new();
        }

        // Pass 2: everything else belongs to one of those storms
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var props) || !TryInt(props, "eventid", out var id) || !storms.TryGetValue(id, out var storm))
                continue;

            var cls = Class(feature);
            var label = Text(props, "polygonlabel") ?? string.Empty;

            if (cls.StartsWith("Line_Line_", StringComparison.Ordinal) && LineCoordinates(feature) is { Length: > 1 } line)
            {
                int.TryParse(cls["Line_Line_".Length..], out var order);
                linesByStorm[id].Add((order, new TrackSegmentDto { Category = label, Points = line }));
            }
            else if (cls == "Poly_Cones")
            {
                foreach (var ring in Rings(feature)) storm.Cone.Add(ring);
            }
            else if (cls.StartsWith("Poly_", StringComparison.Ordinal) && TryLeadingInt(label, out var kmh))
            {
                foreach (var ring in Rings(feature)) storm.WindAreas.Add(new WindAreaDto { KmPerHour = kmh, Ring = ring });
            }
            else if (cls.StartsWith("Point_Polygon_Point_", StringComparison.Ordinal) && Rings(feature).FirstOrDefault() is { Length: > 0 } around)
            {
                storm.ForecastPoints.Add(new ForecastPointDto
                {
                    Label = label,
                    // The middle of the ring's bounds: averaging the vertices would be skewed by the
                    // repeated closing vertex
                    Lat = Round((around.Min(p => p[0]) + around.Max(p => p[0])) / 2),
                    Lng = Round((around.Min(p => p[1]) + around.Max(p => p[1])) / 2)
                });
            }
        }

        foreach (var (id, storm) in storms)
            storm.Track = linesByStorm[id].OrderBy(l => l.Order).Select(l => l.Segment).ToList();

        // The most dangerous first, so a crowded map still leads with what matters
        return storms.Values.OrderByDescending(s => AlertRank(s.AlertLevel)).ThenBy(s => s.Name).ToList();
    }

    private static int AlertRank(string level) => level switch { "Red" => 3, "Orange" => 2, _ => 1 };

    private static string Class(JsonElement feature) =>
        feature.TryGetProperty("properties", out var p) ? Text(p, "Class") ?? string.Empty : string.Empty;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value);
    }

    private static bool TryLeadingInt(string text, out int value)
    {
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static double Round(double value) => Math.Round(value, 2);

    // GeoJSON is [lng, lat]; the map wants [lat, lng]
    private static bool TryPoint(JsonElement feature, out double lat, out double lng)
    {
        lat = lng = 0;
        if (!feature.TryGetProperty("geometry", out var g) || !g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2)
            return false;

        lng = c[0].GetDouble();
        lat = c[1].GetDouble();
        return true;
    }

    private static double[][]? LineCoordinates(JsonElement feature) =>
        feature.TryGetProperty("geometry", out var g) && g.TryGetProperty("coordinates", out var c) ? ToPoints(c) : null;

    // Polygon or MultiPolygon, outer rings only
    private static IEnumerable<double[][]> Rings(JsonElement feature)
    {
        if (!feature.TryGetProperty("geometry", out var g) || !g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array)
            yield break;

        var type = Text(g, "type");
        if (type == "Polygon")
        {
            if (c.GetArrayLength() > 0 && ToPoints(c[0]) is { } ring) yield return ring;
        }
        else if (type == "MultiPolygon")
        {
            foreach (var polygon in c.EnumerateArray())
                if (polygon.ValueKind == JsonValueKind.Array && polygon.GetArrayLength() > 0 && ToPoints(polygon[0]) is { } ring)
                    yield return ring;
        }
    }

    private static double[][]? ToPoints(JsonElement coordinates)
    {
        if (coordinates.ValueKind != JsonValueKind.Array) return null;

        var points = new List<double[]>();
        foreach (var pair in coordinates.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) return null;
            points.Add([Round(pair[1].GetDouble()), Round(pair[0].GetDouble())]);
        }

        return points.ToArray();
    }
}

// Where the raw GeoJSON comes from (the real one asks GDACS; tests use a fake)
public interface ITyphoonSource
{
    Task<string> FetchAsync(CancellationToken cancellationToken = default);
}

public class GdacsTyphoonSource : ITyphoonSource
{
    private readonly HttpClient _http;
    private readonly WeatherOptions _options;

    public GdacsTyphoonSource(HttpClient http, IOptions<WeatherOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<string> FetchAsync(CancellationToken cancellationToken = default) =>
        _http.GetStringAsync(_options.TyphoonSourceUrl, cancellationToken);
}

// Where the last good copy is kept between restarts
public interface ITyphoonStore
{
    Task<(DateTime FetchedAt, List<TyphoonDto> Storms)?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DateTime fetchedAt, List<TyphoonDto> storms, CancellationToken cancellationToken = default);
}

public class DbTyphoonStore : ITyphoonStore
{
    private const string Key = "weather.typhoons";

    private readonly MasidBahaDbContext _db;

    public DbTyphoonStore(MasidBahaDbContext db) => _db = db;

    public async Task<(DateTime FetchedAt, List<TyphoonDto> Storms)?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.CachedDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Key == Key, cancellationToken);
        if (row is null) return null;

        var storms = JsonSerializer.Deserialize<List<TyphoonDto>>(row.Json);
        return storms is null ? null : (DateTime.SpecifyKind(row.FetchedAt, DateTimeKind.Utc), storms);
    }

    public async Task SaveAsync(DateTime fetchedAt, List<TyphoonDto> storms, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(storms);

        // One row, replaced each time
        var updated = await _db.CachedDocuments.Where(d => d.Key == Key)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Json, json).SetProperty(d => d.FetchedAt, fetchedAt), cancellationToken);
        if (updated == 0)
        {
            _db.CachedDocuments.Add(new CachedDocument { Key = Key, Json = json, FetchedAt = fetchedAt });
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

public interface ITyphoonService
{
    // Picks up the copy saved by an earlier run, if there is one, so a restart is not blank
    Task LoadSavedAsync(CancellationToken cancellationToken = default);

    // The latest copy, straight from memory. Null until the first refresh has finished.
    TyphoonsDto? Current { get; }

    // Asks the source for fresh data (slow: GDACS takes about half a minute). Called by the background
    // refresher, never by a visitor's request.
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

// GDACS answers slowly (around 30 seconds for its full list), so visitors never wait for it: a background
// service refreshes this copy on a schedule and every request is answered from memory. If a refresh
// fails the previous copy stays, marked stale once it is clearly out of date.
public class TyphoonService : ITyphoonService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WeatherOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TyphoonService> _logger;

    private volatile Snapshot? _snapshot;

    public TyphoonService(IServiceScopeFactory scopes, IOptions<WeatherOptions> options, TimeProvider time, ILogger<TyphoonService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    private record Snapshot(DateTime FetchedAt, List<TyphoonDto> Storms);

    public TyphoonsDto? Current
    {
        get
        {
            var snapshot = _snapshot;
            if (snapshot is null) return null;

            var age = _time.GetUtcNow().UtcDateTime - snapshot.FetchedAt;

            // Far too old to be useful: better no overlay than a wrong one
            if (age > TimeSpan.FromMinutes(_options.StaleMinutes)) return null;

            // Two missed refreshes in a row: say so, rather than present old data as current
            var stale = age > TimeSpan.FromMinutes(_options.CacheMinutes * 2);
            return new TyphoonsDto { GeneratedAt = snapshot.FetchedAt, Stale = stale, Storms = snapshot.Storms };
        }
    }

    public async Task LoadSavedAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetService<ITyphoonStore>();
        if (store is null || _snapshot is not null) return;

        if (await store.LoadAsync(cancellationToken) is { } saved)
        {
            _snapshot = new Snapshot(saved.FetchedAt, saved.Storms);
            _logger.LogInformation("Loaded saved typhoon data from {FetchedAt:u}", saved.FetchedAt);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ITyphoonSource>();

        var storms = TyphoonParser.Parse(await source.FetchAsync(cancellationToken), _options);
        var fetchedAt = _time.GetUtcNow().UtcDateTime;
        _snapshot = new Snapshot(fetchedAt, storms);

        // Keeping a copy is a bonus: failing to save it must not lose the fresh data
        try
        {
            var store = scope.ServiceProvider.GetService<ITyphoonStore>();
            if (store is not null) await store.SaveAsync(fetchedAt, storms, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not save the typhoon data for the next start");
        }
        _logger.LogInformation("Typhoon data refreshed: {Count} storm(s) near the Philippines", storms.Count);
    }
}
