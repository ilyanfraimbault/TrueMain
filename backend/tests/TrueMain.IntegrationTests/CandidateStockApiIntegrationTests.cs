using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Ops;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The candidate-stock endpoint's contract (#1403): what it answers before anything has
/// ever been snapshotted — which is what prod returns until the step's first run — and
/// that its granularity is a closed set rather than free text.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CandidateStockApiIntegrationTests(PostgresFixture fixture)
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    [Fact]
    public async Task GetStock_ReturnsAnEmptySeriesRatherThanZeros_WhenNothingWasSnapshotted()
    {
        await fixture.ResetDatabaseAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(fixture);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CandidateStockReadModel>(
            "/ops/candidates/stock?granularity=hour&windowDays=7");

        payload.Should().NotBeNull();
        payload!.Buckets.Should().BeEmpty("an unmeasured window is not a window of zeros");
        payload.EarliestSnapshotAtUtc.Should().BeNull();
        payload.LatestSnapshotAtUtc.Should().BeNull();
        payload.WindowDays.Should().Be(7);
    }

    [Theory]
    [InlineData("hour")]
    [InlineData("day")]
    [InlineData("week")]
    public async Task GetStock_AcceptsEveryGranularityThePanelOffers(string granularity)
    {
        await fixture.ResetDatabaseAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/ops/candidates/stock?granularity={granularity}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetStock_RejectsAnUnknownGranularity()
    {
        await fixture.ResetDatabaseAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/candidates/stock?granularity=fortnight");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetStock_ShouldRequireOpsApiKey()
    {
        await fixture.ResetDatabaseAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/candidates/stock?granularity=day");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient CreateClient(TrueMainWebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }
}
