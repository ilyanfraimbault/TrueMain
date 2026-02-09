using Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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

            await db.Database.ExecuteSqlRawAsync(
                """
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
    }

    [Fact]
    public async Task GetOrCreatePerkCatalogIdsAsync_ShouldNotCreateDuplicatesUnderConcurrency()
    {
        await _fixture.ResetDatabaseAsync();

        var keys = new[]
        {
            new PerkCatalogKey(8000, 0, 8005, "primaryStyle"),
            new PerkCatalogKey(8400, 0, 8429, "subStyle")
        };

        var task1 = ResolveCatalogAsync(keys);
        var task2 = ResolveCatalogAsync(keys);

        await Task.WhenAll(task1, task2);
        var map1 = await task1;
        var map2 = await task2;

        map1.Should().HaveCount(2);
        map2.Should().HaveCount(2);
        map1[keys[0]].Should().Be(map2[keys[0]]);
        map1[keys[1]].Should().Be(map2[keys[1]]);

        await using var verifyDb = _fixture.CreateDbContext();
        var catalogCount = await verifyDb.PerkSelectionCatalogs.CountAsync();
        catalogCount.Should().Be(2);
    }

    private async Task<Dictionary<PerkCatalogKey, int>> ResolveCatalogAsync(IReadOnlyCollection<PerkCatalogKey> keys)
    {
        await using var db = _fixture.CreateDbContext();
        var repository = new MatchParticipantRepository(db);
        return await repository.GetOrCreatePerkCatalogIdsAsync(keys, CancellationToken.None);
    }
}
