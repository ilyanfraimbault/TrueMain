namespace TrueMain.IntegrationTests;

/// <summary>
/// Binds every integration test class to a single shared <see cref="PostgresFixture"/>
/// (one Postgres container per assembly instead of one per class). Because the
/// database is shared, the per-test <see cref="PostgresFixture.ResetDatabaseAsync"/>
/// must never run concurrently across classes — membership of this single
/// collection serialises them (xUnit runs a collection's classes sequentially,
/// and <c>xunit.runner.json</c> disables cross-collection parallelism too).
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Integration";
}
