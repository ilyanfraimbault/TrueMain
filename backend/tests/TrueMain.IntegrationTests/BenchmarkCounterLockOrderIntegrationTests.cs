using AwesomeAssertions;
using Core.Lol.Benchmarks;
using Core.Lol.Pace;
using Core.Lol.Ranking;
using Data.Repositories;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #2448: every ingesting account upserts the same hot counter rows, and an upsert holds each
/// row it touched until its transaction ends. Two batches locking overlapping rows in different
/// orders deadlock (40P01), so the order cannot be left to whatever order a caller built its
/// batch in. These writers overlap on every row and shuffle their batches on purpose.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BenchmarkCounterLockOrderIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int Writers = 6;
    private const int Rounds = 4;

    private static readonly string[] Positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];
    private static readonly RankTier[] Tiers = [RankTier.Gold, RankTier.Diamond];

    public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PaceAddCounts_DoesNotDeadlock_WhenConcurrentBatchesOverlapInDifferentOrders()
    {
        var counts = (
            from tier in Tiers
            from position in Positions
            from minute in Enumerable.Range(1, 30)
            from metric in new[] { PaceMetric.Cs, PaceMetric.GoldEarned }
            from bucket in Enumerable.Range(0, 5)
            select new PaceBenchmarkCount(new PaceBenchmarkKey("16.19", tier, position, minute, metric, bucket), 1))
            .ToArray();

        await RaceAsync(counts, (session, batch, now) => session.PaceBenchmarkStats.AddCountsAsync(batch, now, CancellationToken.None));

        await using var verify = fixture.CreateDbContext();
        (await verify.PaceBenchmarkStats.CountAsync()).Should().Be(counts.Length);
        (await verify.PaceBenchmarkStats.SumAsync(stat => stat.Count)).Should().Be(counts.Length * Writers * Rounds);
    }

    [Fact]
    public async Task FormAddCounts_DoesNotDeadlock_WhenConcurrentBatchesOverlapInDifferentOrders()
    {
        var counts = (
            from tier in Tiers
            from position in Positions
            from metric in new[] { FormMetric.Kda, FormMetric.KillParticipation }
            from bucket in Enumerable.Range(0, 150)
            select new FormBenchmarkCount(new FormBenchmarkKey("16.19", tier, position, metric, bucket), 1))
            .ToArray();

        await RaceAsync(counts, (session, batch, now) => session.FormBenchmarkStats.AddCountsAsync(batch, now, CancellationToken.None));

        await using var verify = fixture.CreateDbContext();
        (await verify.FormBenchmarkStats.CountAsync()).Should().Be(counts.Length);
        (await verify.FormBenchmarkStats.SumAsync(stat => stat.Count)).Should().Be(counts.Length * Writers * Rounds);
    }

    private async Task RaceAsync<TCount>(
        TCount[] counts,
        Func<IDataSession, IReadOnlyCollection<TCount>, DateTime, Task> addCounts)
    {
        var sessionFactory = fixture.CreateSessionFactory();
        var random = new Random(2448);
        var now = DateTime.UtcNow;

        for (var round = 0; round < Rounds; round++)
        {
            var batches = Enumerable.Range(0, Writers)
                .Select(_ => Shuffled(counts, random))
                .ToArray();

            await Task.WhenAll(batches.Select(async batch =>
            {
                await using var session = await sessionFactory.CreateAsync(CancellationToken.None);
                await using var transaction = await session.BeginTransactionAsync(CancellationToken.None);
                await addCounts(session, batch, now);
                await transaction.CommitAsync(CancellationToken.None);
            }));
        }
    }

    private static TCount[] Shuffled<TCount>(TCount[] counts, Random random)
    {
        var shuffled = counts.ToArray();
        random.Shuffle(shuffled);
        return shuffled;
    }
}
