using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Persistence;
using MasidBaha.Application.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasidBaha.Tests.Integration;

// The typhoon copy survives a restart: a fresh service instance starts from what the last one saved
[Collection(PostgisCollection.Name)]
public class TyphoonPersistenceTests : IAsyncLifetime
{
    private readonly PostgisFixture _postgis;

    public TyphoonPersistenceTests(PostgisFixture postgis) => _postgis = postgis;

    public Task InitializeAsync() => _postgis.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private class FixtureSource : ITyphoonSource
    {
        public Func<string> Next { get; set; } = () => TyphoonFixture.Json;
        public Task<string> FetchAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next());
    }

    // A new "application instance": its own service, sharing only the database
    private (TyphoonService Service, FixtureSource Source) NewInstance()
    {
        var source = new FixtureSource();
        var services = new ServiceCollection();
        services.AddSingleton<ITyphoonSource>(source);
        services.AddScoped(_ => _postgis.CreateDbContext());
        services.AddScoped<ITyphoonStore, DbTyphoonStore>();
        var provider = services.BuildServiceProvider();

        var service = new TyphoonService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WeatherOptions()),
            TimeProvider.System,
            NullLogger<TyphoonService>.Instance);
        return (service, source);
    }

    [Fact]
    public async Task ARefreshIsSaved_AndAFreshInstanceServesItAtOnce()
    {
        var first = NewInstance().Service;
        await first.RefreshAsync();

        var restarted = NewInstance().Service;
        Assert.Null(restarted.Current);                 // nothing yet: a cold start

        await restarted.LoadSavedAsync();

        var current = restarted.Current!;
        Assert.Equal(["TESTO-26", "MIDDLE-26"], current.Storms.Select(s => s.Name));
        Assert.Equal(2, current.Storms[0].Track.Count);   // the whole storm came back, not just its name
        Assert.NotEmpty(current.Storms[0].Cone);
    }

    [Fact]
    public async Task ASecondRefresh_ReplacesTheSavedCopy_NotAddsToIt()
    {
        var (service, source) = NewInstance();
        await service.RefreshAsync();
        source.Next = () => """{ "features": [] }""";
        await service.RefreshAsync();

        await using var db = _postgis.CreateDbContext();
        Assert.Equal(1, db.CachedDocuments.Count());

        var restarted = NewInstance().Service;
        await restarted.LoadSavedAsync();
        Assert.Empty(restarted.Current!.Storms);
    }

    [Fact]
    public async Task LoadingSaved_DoesNotOverwriteFresherDataAlreadyInMemory()
    {
        await NewInstance().Service.RefreshAsync();     // saves two storms

        var (other, otherSource) = NewInstance();
        otherSource.Next = () => """{ "features": [] }""";
        await other.RefreshAsync();                     // newer, and empty
        await other.LoadSavedAsync();                   // must not bring the old storms back

        Assert.Empty(other.Current!.Storms);
    }

    [Fact]
    public async Task WithNothingSaved_LoadingIsHarmless()
    {
        var service = NewInstance().Service;

        await service.LoadSavedAsync();

        Assert.Null(service.Current);
    }
}
