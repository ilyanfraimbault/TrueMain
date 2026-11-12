using System.Data.Common;
using AwesomeAssertions;
using Data;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using TrueMain.Services.Ops.Candidates;

namespace TrueMain.UnitTests;

/// <summary>
/// The admin candidates list caps its pager total with a row limit (#2304). Limiting an
/// unordered query made EF log <see cref="CoreEventId.RowLimitingOperationWithoutOrderByWarning"/>
/// at Warning, seen in production; the count now walks the list's own order.
///
/// <para>No database is involved: the warning is raised while EF compiles the query, so a
/// context that throws on it and refuses to open a connection tells the two apart.</para>
/// </summary>
public sealed class CandidateListQueryOrderTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("Queued", "euw1", null)]
    [InlineData(null, null, "Name#TAG")]
    public async Task TheCappedTotalCompilesWithoutTheRowLimitWithoutOrderByWarning(
        string? status, string? platformId, string? search)
    {
        await using var db = CreateContext();
        var service = new CandidateQueryService(db, Substitute.For<ISeedRequestStore>());

        var read = () => service.GetCandidatesAsync(status, platformId, search, page: 2, pageSize: 25, CancellationToken.None);

        await read.Should().ThrowAsync<QueryReachedTheDatabase>();
    }

    private static TrueMainDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning))
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
