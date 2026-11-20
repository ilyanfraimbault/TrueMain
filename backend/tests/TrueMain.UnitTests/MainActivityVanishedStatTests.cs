using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Persistence;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

/// <summary>
/// The main activity check loads a batch's stat rows tracked, spends minutes on mastery calls,
/// then saves every verdict at once. A row deleted in between (main analysis dropping a
/// champion, an account invalidation) made that save throw <see cref="DbUpdateConcurrencyException"/>,
/// seen in preprod, and the whole batch lost its verdicts and its stamps. A vanished row has
/// nothing left to update: the run drops it and saves the rest.
/// </summary>
public sealed class MainActivityVanishedStatTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 17, 5, 30, 0, TimeSpan.Zero);

    private static readonly AccountKey Account = new("KR", "puuid-a");

    [Fact]
    public async Task RunCoreAsync_SavesTheRest_WhenAStatRowVanishedSinceItWasLoaded()
    {
        await using var db = CreateContext();
        var vanished = db.Entry(new MainChampionStat { Id = Guid.NewGuid(), PlatformId = "KR", Puuid = "puuid-a" });
        vanished.State = EntityState.Modified;
        var harness = new Harness();
        harness.Session.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new DbUpdateConcurrencyException("expected to affect 1 row(s), but actually affected 0", [UpdateEntryOf(vanished)]),
                _ => Task.FromResult(1));

        var summary = await harness.RunAsync();

        summary.AccountsChecked.Should().Be(1);
        vanished.State.Should().Be(EntityState.Detached);
        await harness.Session.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await harness.Accounts.Received(1).StampLastActivityCheckAsync(
            Arg.Is<IReadOnlyCollection<AccountKey>>(keys => keys.SequenceEqual(new[] { Account })),
            NowUtc.UtcDateTime,
            Arg.Any<CancellationToken>());
        await harness.Transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_Fails_WhenTheSaveKeepsMissingRows()
    {
        var harness = new Harness();
        harness.Session.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("expected to affect 1 row(s), but actually affected 0", Array.Empty<IUpdateEntry>()));

        var run = harness.RunAsync;

        await run.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await harness.Transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // The exception carries EF's own update entries; a test reaches the one behind an
    // EntityEntry through the infrastructure accessor, as EF does when it throws it.
#pragma warning disable EF1001
    private static IUpdateEntry UpdateEntryOf(EntityEntry entry) => entry.GetInfrastructure();
#pragma warning restore EF1001

    private static TrueMainDbContext CreateContext()
        => new(new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-unit;Username=none;Password=none")
            .Options);

    private sealed class Harness
    {
        private readonly MainActivityProcess _process;

        public Harness()
        {
            Accounts.GetAccountsForActivityCheckAsync(
                    Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new List<AccountKey> { Account }));

            var stats = Substitute.For<IMainChampionStatRepository>();
            stats.GetByAccountsAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new Dictionary<AccountKey, List<MainChampionStat>>()));

            Session.RiotAccounts.Returns(Accounts);
            Session.MainChampionStats.Returns(stats);
            Session.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Transaction));

            var sessionFactory = Substitute.For<IDataSessionFactory>();
            sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Session));

            var platformClient = Substitute.For<IRiotPlatformClient>();
            platformClient.GetChampionMasteriesAsync(
                    Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new List<RiotChampionMasteryDto>()));

            _process = new MainActivityProcess(
                NullLogger<MainActivityProcess>.Instance,
                platformClient,
                sessionFactory,
                new FixedTimeProvider(NowUtc),
                Microsoft.Extensions.Options.Options.Create(new MainActivityOptions()));
        }

        public IDataSession Session { get; } = Substitute.For<IDataSession>();

        public IRiotAccountRepository Accounts { get; } = Substitute.For<IRiotAccountRepository>();

        public IDataTransaction Transaction { get; } = Substitute.For<IDataTransaction>();

        public async Task<MainActivitySummary> RunAsync()
        {
            var summary = await _process.RunCoreAsync(RunInput.Whole, CancellationToken.None);
            return summary.Should().BeOfType<MainActivitySummary>().Subject;
        }
    }
}
