using System.Data.Common;
using AwesomeAssertions;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TrueMain.Options;
using TrueMain.Services.Champions.Draft;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Synergies;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

/// <summary>
/// The whole-scope totals folded with <c>GroupBy(_ =&gt; 1)</c>: one group at most, read
/// back as a single row. Taking it with <c>FirstOrDefault</c> made EF log
/// <see cref="CoreEventId.FirstWithoutOrderByAndFilterWarning"/> at Warning, seen in
/// production; the fold has no order to give, so the read states that it expects one row.
///
/// <para>No database is involved: the warning is raised while EF compiles the query, so a
/// context that throws on it and refuses to open a connection tells the two apart.</para>
/// </summary>
public sealed class SingleRowAggregateQueryTests
{
    [Fact]
    public async Task TheDraftLaneAverageCompilesWithoutTheFirstWithoutOrderByWarning()
    {
        await using var db = CreateContext();
        var reader = new DraftLaneReader(db, TestChampionReadCache.PassThrough());

        var read = () => reader.ReadLaneAverageAsync("TOP", DraftPatchScope.Every, eloBracket: null, CancellationToken.None);

        await read.Should().ThrowAsync<QueryReachedTheDatabase>();
    }

    [Fact]
    public async Task TheTrioPairTotalsCompileWithoutTheFirstWithoutOrderByWarning()
    {
        await using var db = CreateContext();
        var service = new ChampionSynergyQueryService(
            db,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions()),
            Microsoft.Extensions.Options.Options.Create(new ChampionsListOptions()),
            TestChampionReadCache.PassThrough());

        var read = () => service.GetTrioSynergiesAsync(
            new ChampionScope(championId: 1, position: "TOP", patch: null),
            partnerChampionId: 2, partnerPosition: "JUNGLE", CancellationToken.None);

        await read.Should().ThrowAsync<QueryReachedTheDatabase>();
    }

    private static TrueMainDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.FirstWithoutOrderByAndFilterWarning))
            .AddInterceptors(new RefuseConnections())
            .Options;

        return new TrueMainDbContext(options);
    }

    private sealed class QueryReachedTheDatabase : Exception;

    /// <summary>Stops a query once EF has compiled it, before any network call.</summary>
    private sealed class RefuseConnections : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
            => throw new QueryReachedTheDatabase();

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
            => throw new QueryReachedTheDatabase();
    }
}
