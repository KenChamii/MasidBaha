using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasidBaha.Application.Weather;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MasidBaha.Tests.Integration;

// The typhoon endpoint through the real pipeline, with GDACS replaced by a scripted source
[Collection(PostgisCollection.Name)]
public class WeatherEndpointTests : IAsyncLifetime
{
    private const string Url = "/api/v1/weather/typhoons";

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public WeatherEndpointTests(PostgisFixture postgis) => _postgis = postgis;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgis.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private class FixtureSource : ITyphoonSource
    {
        public Task<string> FetchAsync(CancellationToken cancellationToken = default) => Task.FromResult(TyphoonFixture.Json);
    }

    private class BrokenSource : ITyphoonSource
    {
        public Task<string> FetchAsync(CancellationToken cancellationToken = default) => throw new HttpRequestException("GDACS is down");
    }

    private WebApplicationFactory<Program> With<TSource>() where TSource : class, ITyphoonSource =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITyphoonSource>();
            services.AddSingleton<ITyphoonSource, TSource>();
        }));

    // The background refresher fills the copy shortly after start-up
    private static async Task<HttpResponseMessage> WaitForAnswerAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await client.GetAsync(Url);
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable) return response;
            await Task.Delay(100);
        }

        return await client.GetAsync(Url);
    }

    [Fact]
    public async Task ServesTheStormsNearThePhilippines_FromMemory()
    {
        using var factory = With<FixtureSource>();

        var response = await WaitForAnswerAsync(factory.CreateClient());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=300", response.Headers.CacheControl!.ToString());

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("stale").GetBoolean());
        var storms = body.GetProperty("storms");
        Assert.Equal(2, storms.GetArrayLength());
        Assert.Equal("TESTO-26", storms[0].GetProperty("name").GetString());
        Assert.Equal("Red", storms[0].GetProperty("alertLevel").GetString());
        Assert.Equal(2, storms[0].GetProperty("track").GetArrayLength());
    }

    [Fact]
    public async Task WhenNothingHasLoaded_SaysToTryAgainShortly()
    {
        using var factory = With<BrokenSource>();
        var client = factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("30", response.Headers.RetryAfter!.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task NoStormsAtAll_IsAnAnswer_NotAnError()
    {
        var response = await WaitForAnswerAsync(_factory.CreateClient());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storms").GetArrayLength());
    }
}
