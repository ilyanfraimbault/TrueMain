using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data;
using Data.Entities;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Processes.Components.Retention;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The light per-patch record (#1956) must answer "Trend by patch" exactly as the full
/// aggregates did, per elo bracket, and keep answering it once those are purged — the
/// acceptance of #1956, of #1957 (the trend endpoint reads the record) and what #1958
/// (purge) rests on.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionPatchStatIntegrationTests : IAsyncLifetime
{
    private const int QueueId = 420;
    private const int ChampionId = 157;
    private const string Position = "MIDDLE";

    private static readonly string[] Patches = ["16.3", "16.4", "16.5"];
    private static readonly string[] Brackets = [EloBracket.Gold, EloBracket.Platinum, EloBracket.Diamond];
    private static readonly int[] Champions = [ChampionId, 238, 103];
    private static readonly string[] Positions = [Position, "BOTTOM"];

    private readonly PostgresFixture _fixture;

    public ChampionPatchStatIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LightRecord_ReproducesTheTrendRatesPerBracket_AndOutlivesTheFullAggregates()
    {
        await SeedFullAggregatesAsync();
        var factory = new TestDbContextFactory(_fixture);
        var persister = new ChampionPatternAggregatePersister(factory, new ChampionDimensionResolver(factory));

        (await persister.BackfillPatchStatsIfEmptyAsync(DateTime.UtcNow, CancellationToken.None))
            .Should().BeGreaterThan(0, "the record is filled from the scopes already there");

        // What the chart shows: the trend endpoint, reading the light record (#1957).
        var served = await GetServedTrendAsync();
        served.Points.Select(point => point.Patch).Should().Equal(Patches);

        Dictionary<string, TrendFigures> unfilteredBefore;
        var perBracketBefore = new Dictionary<string, Dictionary<string, TrendFigures>>(StringComparer.Ordinal);
        await using (var db = _fixture.CreateDbContext())
        {
            // The endpoint serves what the full aggregates gave before the read moved.
            ShouldMatchServed(await FromScopesAsync(db, brackets: null), served);

            unfilteredBefore = await FromLightRecordAsync(db, brackets: null);
            ShouldMatchServed(unfilteredBefore, served);

            // The rank filter the record exists to honour: one bracket at a time, and a
            // multi-bracket set like GOLD_PLUS, the light record equals the full aggregates.
            foreach (var bracket in Brackets)
            {
                var light = await FromLightRecordAsync(db, [bracket]);
                light.Should().BeEquivalentTo(await FromScopesAsync(db, [bracket]), $"bracket {bracket}");
                light.Values.Should().OnlyContain(figures => figures.BanRate != null);
                perBracketBefore[bracket] = light;
            }

            (await FromLightRecordAsync(db, [EloBracket.Platinum, EloBracket.Diamond]))
                .Should().BeEquivalentTo(await FromScopesAsync(db, [EloBracket.Platinum, EloBracket.Diamond]));
        }

        // Aggregate retention keeping one patch: the full aggregates of 16.3 and 16.4 go.
        await AggregateRetention.DeleteExpiredAsync(factory, 1, NullLogger.Instance, CancellationToken.None);

        await using (var db = _fixture.CreateDbContext())
        {
            (await db.ChampionAggregateScopes.Select(scope => scope.GameVersion).Distinct().ToListAsync())
                .Should().Equal("16.5");

            // The record and the ban pair are the light level: retention leaves them whole,
            // so every patch still answers with the figures the chart showed before.
            (await FromLightRecordAsync(db, brackets: null)).Should().BeEquivalentTo(unfilteredBefore);
            foreach (var bracket in Brackets)
            {
                (await FromLightRecordAsync(db, [bracket])).Should().BeEquivalentTo(perBracketBefore[bracket]);
            }
        }
    }

    [Fact]
    public async Task TrendEndpoint_ServesTheFullAggregateFiguresPerRankFilter_BeforeAndAfterThePurge()
    {
        await SeedFullAggregatesAsync();
        await LightRecordSeed.BackfillFromScopesAsync(_fixture);

        // Every rank, each stored bracket, and an "and above" filter: the endpoint, now on
        // the light record, against ChampionTrendQueryService's arithmetic over the full
        // aggregates — the read it replaced (#1957).
        string?[] filters = [null, .. Brackets, "PLATINUM_PLUS"];
        var servedBefore = new Dictionary<string, ChampionTrendReadModel>(StringComparer.Ordinal);
        foreach (var filter in filters)
        {
            var served = await GetServedTrendAsync(filter);
            served.Points.Select(point => point.Patch).Should().Equal(Patches, filter ?? "every rank");

            await using var db = _fixture.CreateDbContext();
            var bands = filter is null ? null : EloBracket.ResolveFilterOrEmpty(filter)!.ToArray();
            ShouldMatchServed(await FromScopesAsync(db, bands), served);
            servedBefore[filter ?? EloBracket.All] = served;
        }

        // A filter narrows the figures: GOLD alone is not every rank.
        servedBefore[EloBracket.Gold].Points.Select(point => point.Games)
            .Should().NotEqual(servedBefore[EloBracket.All].Points.Select(point => point.Games));

        // The full aggregates of 16.3 and 16.4 go; the served series does not change.
        await AggregateRetention.DeleteExpiredAsync(
            new TestDbContextFactory(_fixture), 1, NullLogger.Instance, CancellationToken.None);

        foreach (var filter in filters)
        {
            (await GetServedTrendAsync(filter)).Should().BeEquivalentTo(servedBefore[filter ?? EloBracket.All]);
        }
    }

    [Fact]
    public async Task TrendEndpoint_RejectsAnUnknownRankFilter()
    {
        await using var api = new TrueMainWebApplicationFactory<Program>(
            _fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync($"/champions/{ChampionId}/trend?position=middle&eloBracket=WOOD");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Backfill_RunsOnlyOnAnEmptyRecord()
    {
        await SeedFullAggregatesAsync();
        var factory = new TestDbContextFactory(_fixture);
        var persister = new ChampionPatternAggregatePersister(factory, new ChampionDimensionResolver(factory));

        var written = await persister.BackfillPatchStatsIfEmptyAsync(DateTime.UtcNow, CancellationToken.None);

        // Once filled, the fold owns the record: a second backfill would double every row.
        (await persister.BackfillPatchStatsIfEmptyAsync(DateTime.UtcNow, CancellationToken.None)).Should().Be(0);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionPatchStats.CountAsync()).Should().Be(written);
        (await db.ChampionPatchStats.SumAsync(row => row.Games))
            .Should().Be(await db.ChampionAggregateScopes.SumAsync(scope => scope.Games));
    }

    private static void ShouldMatchServed(IReadOnlyDictionary<string, TrendFigures> figures, ChampionTrendReadModel served)
    {
        figures.Keys.Should().BeEquivalentTo(served.Points.Select(point => point.Patch));
        foreach (var point in served.Points)
        {
            var expected = figures[point.Patch];
            point.Games.Should().Be(expected.Games, point.Patch);
            point.WinRate.Should().BeApproximately(expected.WinRate, 1e-12, point.Patch);
            point.PickRate.Should().BeApproximately(expected.PickRate, 1e-12, point.Patch);
            point.BanRate.Should().NotBeNull();
            point.BanRate!.Value.Should().BeApproximately(expected.BanRate!.Value, 1e-12, point.Patch);
        }
    }

    private async Task<ChampionTrendReadModel> GetServedTrendAsync(string? eloBracket = null)
    {
        await using var api = new TrueMainWebApplicationFactory<Program>(
            _fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var query = eloBracket is null ? string.Empty : $"&eloBracket={eloBracket}";
        var trend = await client.GetFromJsonAsync<ChampionTrendReadModel>(
            $"/champions/{ChampionId}/trend?position=middle{query}");
        trend.Should().NotBeNull();
        return trend!;
    }

    /// <summary>
    /// <c>ChampionTrendQueryService</c>'s arithmetic over the full aggregates, the read it
    /// replaced (#1957) — mains only, lane total over every champion — with its rank filter.
    /// </summary>
    private static async Task<Dictionary<string, TrendFigures>> FromScopesAsync(TrueMainDbContext db, string[]? brackets)
    {
        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == QueueId && scope.Position == Position && scope.IsMain)
            .Where(scope => brackets == null || brackets.Contains(scope.EloBracket))
            .Select(scope => new PatchRow(scope.GameVersion, scope.ChampionId, scope.Games, scope.Wins))
            .ToListAsync();
        return await ToFiguresAsync(db, rows, brackets);
    }

    /// <summary>The same arithmetic over the light record alone.</summary>
    private static async Task<Dictionary<string, TrendFigures>> FromLightRecordAsync(TrueMainDbContext db, string[]? brackets)
    {
        var rows = await db.ChampionPatchStats
            .AsNoTracking()
            .Where(stat => stat.QueueId == QueueId && stat.Position == Position && stat.IsMain)
            .Where(stat => brackets == null || brackets.Contains(stat.EloBracket))
            .Select(stat => new PatchRow(stat.Patch, stat.ChampionId, stat.Games, stat.Wins))
            .ToListAsync();
        return await ToFiguresAsync(db, rows, brackets);
    }

    private static async Task<Dictionary<string, TrendFigures>> ToFiguresAsync(
        TrueMainDbContext db,
        List<PatchRow> laneRows,
        string[]? brackets)
    {
        // Unfiltered reads the stored ALL band, a bracket set sums its bands (#920).
        var banBands = brackets ?? [EloBracket.All];
        var banMatches = await db.BanScopeTotals.AsNoTracking()
            .Where(total => banBands.Contains(total.EloBracket))
            .GroupBy(total => total.Patch)
            .Select(group => new { Patch = group.Key, Matches = group.Sum(total => total.Matches) })
            .ToDictionaryAsync(entry => entry.Patch, entry => entry.Matches);
        var bans = await db.ChampionBanStats.AsNoTracking()
            .Where(stat => stat.ChampionId == ChampionId && banBands.Contains(stat.EloBracket))
            .GroupBy(stat => stat.Patch)
            .Select(group => new { Patch = group.Key, Bans = group.Sum(stat => stat.Bans) })
            .ToDictionaryAsync(entry => entry.Patch, entry => entry.Bans);

        return laneRows
            .Where(row => row.ChampionId == ChampionId)
            .GroupBy(row => row.Patch)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var games = group.Sum(row => row.Games);
                    var laneTotal = laneRows.Where(row => row.Patch == group.Key).Sum(row => (long)row.Games);
                    double? banRate = banMatches.TryGetValue(group.Key, out var matches) && matches > 0
                        ? (double)bans.GetValueOrDefault(group.Key) / matches
                        : null;
                    return new TrendFigures(
                        games,
                        (double)group.Sum(row => row.Wins) / games,
                        (double)games / laneTotal,
                        banRate);
                });
    }

    /// <summary>
    /// Scopes on three patches for three accounts on three platforms — one of them a
    /// non-main, which the trend never counts — across champions, lanes and brackets, with
    /// uneven counts so a wrong grouping shows; plus the ban pair, per band and ALL.
    /// </summary>
    private async Task SeedFullAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        var accounts = new[] { ("KR", true), ("EUW1", true), ("NA1", false) }
            .Select((entry, index) => (
                Account: new RiotAccountBuilder()
                    .WithPlatformId(entry.Item1)
                    .WithPuuid($"patch-stat-{index}")
                    .WithGameName($"patch-stat-{index}")
                    .Build(),
                IsMain: entry.Item2))
            .ToList();
        db.RiotAccounts.AddRange(accounts.Select(entry => entry.Account));

        var seed = 0;
        foreach (var (account, isMain) in accounts)
        {
            foreach (var patch in Patches)
            {
                foreach (var champion in Champions)
                {
                    foreach (var position in Positions)
                    {
                        foreach (var bracket in Brackets)
                        {
                            seed++;
                            if (seed % 5 == 0)
                            {
                                continue;
                            }

                            var games = 3 + (seed * 7 % 23);
                            db.ChampionAggregateScopes.Add(new ChampionAggregateScope
                            {
                                Id = Guid.NewGuid(),
                                RiotAccountId = account.Id,
                                ChampionId = champion,
                                GameVersion = patch,
                                PlatformId = account.PlatformId,
                                QueueId = QueueId,
                                Position = position,
                                EloBracket = bracket,
                                IsMain = isMain,
                                Games = games,
                                Wins = seed * 3 % (games + 1),
                                LastGameStartTimeUtc = now,
                                AggregatedAtUtc = now,
                            });
                        }
                    }
                }
            }
        }

        foreach (var patch in Patches)
        {
            foreach (var (band, index) in Brackets.Append(EloBracket.All).Select((band, index) => (band, index)))
            {
                var matches = 100 + (index * 37) + patch.Length;
                db.BanScopeTotals.Add(new BanScopeTotal
                {
                    Id = Guid.NewGuid(), Patch = patch, EloBracket = band, Matches = matches, AggregatedAtUtc = now,
                });
                db.ChampionBanStats.Add(new ChampionBanStat
                {
                    Id = Guid.NewGuid(), ChampionId = ChampionId, Patch = patch, EloBracket = band,
                    Bans = matches / (index + 3), AggregatedAtUtc = now,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private sealed record PatchRow(string Patch, int ChampionId, int Games, int Wins);

    private sealed record TrendFigures(int Games, double WinRate, double PickRate, double? BanRate);
}
