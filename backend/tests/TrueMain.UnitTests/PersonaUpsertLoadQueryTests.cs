using System.Data.Common;
using AwesomeAssertions;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.Requests.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.UnitTests;

/// <summary>
/// The persona upsert loads a persona with both its links and its accounts. Two collection
/// includes in one query without a splitting behaviour made EF log
/// <see cref="RelationalEventId.MultipleCollectionIncludeWarning"/> at Warning, seen in
/// production; the load splits like the removal's does.
///
/// <para>No database is involved: the warning is raised while EF compiles the query, so a
/// context that throws on it and refuses to open a connection tells the two apart.</para>
/// </summary>
public sealed class PersonaUpsertLoadQueryTests
{
    [Fact]
    public async Task ThePersonaLoadCompilesWithoutTheMultipleCollectionIncludeWarning()
    {
        await using var db = CreateContext();
        var service = new PersonaUpsertService(
            db,
            seedRequestService: null!,
            conflicts: null!,
            autoMerge: null!,
            NullLogger<PersonaUpsertService>.Instance);

        var upsert = () => service.UpsertAsync(
            new PersonaUpsertEntry { Slug = "some-player", DisplayName = "Some Player", Source = "manual" },
            CancellationToken.None);

        await upsert.Should().ThrowAsync<QueryReachedTheDatabase>();
    }

    private static TrueMainDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .ConfigureWarnings(warnings => warnings.Throw(
                RelationalEventId.MultipleCollectionIncludeWarning,
                CoreEventId.RowLimitingOperationWithoutOrderByWarning,
                CoreEventId.FirstWithoutOrderByAndFilterWarning))
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
