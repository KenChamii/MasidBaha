using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Tests.Integration;

// A phone that loses signal sends the same report again later. That must not make two reports.
[Collection(PostgisCollection.Name)]
public class ReportRetryTests : IAsyncLifetime
{
    private const string Api = "/api/v1";

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public ReportRetryTests(PostgisFixture postgis) => _postgis = postgis;

    public async Task InitializeAsync()
    {
        await _postgis.ResetAsync();
        _factory = new ApiFactory(_postgis.ConnectionString);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static object Report(Guid? clientRequestId, double lat = 14.65) =>
        new { lat, lng = 121.10, severity = 3, notes = "Water is rising", clientRequestId };

    private static async Task<Guid> IdOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

    private async Task<int> RowCountAsync()
    {
        await using var db = _postgis.CreateDbContext();
        return await db.FloodReports.CountAsync();
    }

    [Fact]
    public async Task SendingTheSameReportTwice_SavesItOnce_AndAnswersTheSameBothTimes()
    {
        var client = _factory.CreateClient();
        var requestId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync($"{Api}/flood-reports", Report(requestId));
        var second = await client.PostAsJsonAsync($"{Api}/flood-reports", Report(requestId));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await IdOf(first), await IdOf(second));
        Assert.Equal(1, await RowCountAsync());
    }

    [Fact]
    public async Task TheRetry_DoesNotAlertEveryoneAgain()
    {
        var client = _factory.CreateClient();
        var requestId = Guid.NewGuid();

        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(requestId));
        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(requestId));

        Assert.Single(_factory.Push.Enqueued);
    }

    [Fact]
    public async Task TwoRequestsArrivingAtTheSameTime_StillMakeOneReport()
    {
        var client = _factory.CreateClient();
        // The device already has its session (a visitor's first report gives it one); a retry carries that cookie
        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(Guid.NewGuid(), lat: 14.60));
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync($"{Api}/flood-reports", Report(requestId))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single((await Task.WhenAll(responses.Select(IdOf))).Distinct());
        Assert.Equal(2, await RowCountAsync());   // the first report, and the one sent four times
    }

    [Fact]
    public async Task DifferentRequestIds_AreDifferentReports()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(Guid.NewGuid()));
        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(Guid.NewGuid()));

        Assert.Equal(2, await RowCountAsync());
    }

    [Fact]
    public async Task ReportsWithoutAnId_BehaveAsBefore()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(null));
        await client.PostAsJsonAsync($"{Api}/flood-reports", Report(null));

        Assert.Equal(2, await RowCountAsync());
    }

    [Fact]
    public async Task TheRetryOfAFirstEverReport_StillMatches_EvenThoughTheLostAnswerTookTheSessionCookieWithIt()
    {
        var requestId = Guid.NewGuid();

        // two different browsers stand in for "the first answer (and its cookie) never arrived"
        var first = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports", Report(requestId));
        var retry = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports", Report(requestId));

        Assert.Equal(await IdOf(first), await IdOf(retry));
        Assert.Equal(1, await RowCountAsync());
    }
}
