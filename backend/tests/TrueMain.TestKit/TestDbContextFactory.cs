using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.TestKit;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> impl that hands back contexts
/// built from the shared <see cref="PostgresFixture"/>. Lets services that
/// accept an <c>IDbContextFactory&lt;TrueMainDbContext&gt;</c> (aggregate
/// persister, retention process, …) be unit-tested against the test
/// container without spinning up the full DI graph.
/// </summary>
public sealed class TestDbContextFactory(PostgresFixture fixture) : IDbContextFactory<TrueMainDbContext>
{
    public TrueMainDbContext CreateDbContext() => fixture.CreateDbContext();

    public Task<TrueMainDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(fixture.CreateDbContext());
}
