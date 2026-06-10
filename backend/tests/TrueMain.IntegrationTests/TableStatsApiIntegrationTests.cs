using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class TableStatsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public TableStatsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetTableStatsAsync_ShouldReturnPublicSchemaTableSizes()
    {
        // The schema is migrated by the fixture; reset just clears data. The
        // catalog still reports every public table.
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);

        var response = await client.GetAsync("/ops/db/tables");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        document.RootElement[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "tableName",
                "rowEstimate",
                "totalBytes",
                "tableBytes",
                "indexBytes"
            ]);

        var rows = await response.Content.ReadFromJsonAsync<IReadOnlyList<TableStatRowTestContract>>();
        rows.Should().NotBeNull();

        // The core mapped tables must be present in the public schema.
        rows!.Select(row => row.TableName).Should().Contain(
            ["matches", "match_participants", "main_champion_stats", "process_runs", "riot_accounts"]);

        // Every reported table has sane, non-negative byte figures and total >=
        // table + index (TOAST makes it >=, never <).
        rows.Should().OnlyContain(row =>
            row.TotalBytes >= 0
            && row.TableBytes >= 0
            && row.IndexBytes >= 0
            && row.RowEstimate >= 0
            && row.TotalBytes >= row.TableBytes + row.IndexBytes);
    }

    [Fact]
    public async Task GetTableStatsAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/db/tables");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class TableStatRowTestContract
    {
        public string TableName { get; init; } = string.Empty;

        public long RowEstimate { get; init; }

        public long TotalBytes { get; init; }

        public long TableBytes { get; init; }

        public long IndexBytes { get; init; }
    }
}
