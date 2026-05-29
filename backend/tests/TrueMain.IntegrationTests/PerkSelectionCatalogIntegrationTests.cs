using Data;
using Data.Repositories;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace TrueMain.IntegrationTests;

public sealed class PerkSelectionCatalogIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string PreNormalizationMigration = "20260209140655_AddMatchIngestLeaseAndTimelineIngested";
    private readonly PostgresFixture _fixture;

    public PerkSelectionCatalogIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_ShouldBackfillCatalogAndReferences()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            await db.Database.EnsureDeletedAsync();
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreNormalizationMigration);

            // Seed parent matches first so the later
            // PurgeOrphanPerkSelectionsAndFk migration's orphan cleanup
            // does not wipe these rows before the catalog backfill runs.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO matches
                    ("Id", "PlatformId", "QueueId", "GameStartTimeUtc",
                     "GameDurationSeconds", "GameVersion")
                VALUES
                    ('EUW1_1', 'EUW1', 420, NOW(), 1800, '14.1.1'),
                    ('EUW1_2', 'EUW1', 420, NOW(), 1800, '14.1.1');

                INSERT INTO participant_perk_selections
                    ("Id", "MatchId", "ParticipantId", "StyleId", "StyleDescription", "SelectionIndex", "PerkId")
                VALUES
                    ('00000000-0000-0000-0000-000000000001', 'EUW1_1', 1, 8000, 'primaryStyle', 0, 8005),
                    ('00000000-0000-0000-0000-000000000002', 'EUW1_1', 1, 8000, 'primaryStyle', 1, 9111),
                    ('00000000-0000-0000-0000-000000000003', 'EUW1_2', 2, 8000, 'primaryStyle', 0, 8005);
                """);

            await migrator.MigrateAsync();
        }

        await using var verifyDb = _fixture.CreateDbContext();
        var links = await verifyDb.ParticipantPerkSelections.AsNoTracking().ToListAsync();
        var catalogById = await verifyDb.PerkSelectionCatalogs.AsNoTracking().ToDictionaryAsync(item => item.Id);

        links.Should().HaveCount(3);
        catalogById.Should().HaveCount(2);
        links.Should().OnlyContain(link => catalogById.ContainsKey(link.PerkSelectionCatalogId));

        var reconstructed = links
            .Select(link =>
            {
                var catalog = catalogById[link.PerkSelectionCatalogId];
                return new
                {
                    link.MatchId,
                    link.ParticipantId,
                    catalog.StyleId,
                    catalog.SelectionIndex,
                    catalog.PerkId,
                    catalog.StyleDescription
                };
            })
            .ToList();

        reconstructed.Should().ContainEquivalentOf(new
        {
            MatchId = "EUW1_1",
            ParticipantId = 1,
            StyleId = 8000,
            SelectionIndex = 0,
            PerkId = 8005,
            StyleDescription = "primaryStyle"
        });

        await AssertParticipantPerkSelectionsSchemaAsync();
    }

    [Fact]
    public async Task GetOrCreatePerkCatalogIdsAsync_ShouldNotCreateDuplicatesUnderConcurrency()
    {
        await _fixture.ResetDatabaseAsync();

        PerkCatalogKey[] keys =
        [
            new(8000, 0, 8005, "primaryStyle"),
            new(8400, 0, 8429, "subStyle")
        ];

        Task<Dictionary<PerkCatalogKey, int>> task1 = ResolveCatalogAsync(keys);
        Task<Dictionary<PerkCatalogKey, int>> task2 = ResolveCatalogAsync(keys);

        await Task.WhenAll(task1, task2);
        Dictionary<PerkCatalogKey, int> map1 = await task1;
        Dictionary<PerkCatalogKey, int> map2 = await task2;

        map1.Should().HaveCount(2);
        map2.Should().HaveCount(2);
        map1[keys[0]].Should().Be(map2[keys[0]]);
        map1[keys[1]].Should().Be(map2[keys[1]]);

        await using TrueMainDbContext verifyDb = _fixture.CreateDbContext();
        int catalogCount = await verifyDb.PerkSelectionCatalogs.CountAsync();
        catalogCount.Should().Be(2);
    }

    private async Task<Dictionary<PerkCatalogKey, int>> ResolveCatalogAsync(IReadOnlyCollection<PerkCatalogKey> keys)
    {
        await using var db = _fixture.CreateDbContext();
        var repository = new MatchParticipantRepository(db);
        return await repository.GetOrCreatePerkCatalogIdsAsync(keys, CancellationToken.None);
    }

    private async Task AssertParticipantPerkSelectionsSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'participant_perk_selections'
            ORDER BY ordinal_position;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.Should().Contain("PerkSelectionCatalogId");
        columns.Should().NotContain("StyleId");
        columns.Should().NotContain("SelectionIndex");
        columns.Should().NotContain("PerkId");
        columns.Should().NotContain("StyleDescription");
    }
}
