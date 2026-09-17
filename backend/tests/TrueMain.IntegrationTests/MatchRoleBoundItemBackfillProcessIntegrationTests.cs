using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers <see cref="MatchRoleBoundItemBackfillProcess"/> (#1612): bot-lane rows stored
/// before Riot's role-bound slot was recorded get their boots back from the item
/// timeline, while other roles and rows that already carry Riot's value are left alone.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchRoleBoundItemBackfillProcessIntegrationTests
{
    private const string MatchId = "m-role-bound";
    private const int Greaves = 3006;
    private const int Gluttonous = 3008;
    private const int Legendary = 3153;

    private readonly PostgresFixture _fixture;

    public MatchRoleBoundItemBackfillProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ResolvesLegacyBotLaneRows_AndLeavesTheRestAlone()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            db.Matches.Add(new MatchBuilder().WithId(MatchId).Build());
            db.MatchParticipants.AddRange(
                Participant(1, "BOTTOM", roleBound: null, item0: Legendary, boughtBoots: Greaves),
                Participant(2, "BOTTOM", roleBound: null, item0: Greaves, boughtBoots: Greaves),
                Participant(3, "BOTTOM", roleBound: null, item0: Legendary, boughtBoots: null),
                Participant(4, "MIDDLE", roleBound: null, item0: Legendary, boughtBoots: Greaves),
                Participant(5, "BOTTOM", roleBound: Gluttonous, item0: Legendary, boughtBoots: Greaves));
            await db.SaveChangesAsync();
        }

        await RunBackfillAsync();

        var slots = await RoleBoundByParticipantAsync();
        slots[1].Should().Be(Greaves);
        slots[2].Should().Be(0, "the boots never left the inventory slots");
        slots[3].Should().Be(0, "a resolved row with nothing to infer is still resolved");
        slots[4].Should().BeNull("only a bot laner's slot can be recovered from the timeline");
        slots[5].Should().Be(Gluttonous, "Riot's own value is never overwritten");

        // A second run finds nothing left to drain.
        await RunBackfillAsync();
        (await RoleBoundByParticipantAsync()).Should().BeEquivalentTo(slots);
    }

    private static MatchParticipant Participant(
        int participantId, string teamPosition, int? roleBound, int item0, int? boughtBoots)
        => new()
        {
            MatchId = MatchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{participantId}",
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = 157,
            TeamId = participantId <= 5 ? 100 : 200,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
            Role = "SOLO",
            ChampLevel = 16,
            Item0 = item0,
            RoleBoundItemId = roleBound,
            ItemEvents = boughtBoots is { } boots
                ? [new ItemEvent { TimestampMs = 300_000, EventType = ItemEventTypes.Purchased, ItemId = boots }]
                : [],
            SkillEvents = []
        };

    private async Task<Dictionary<int, int?>> RoleBoundByParticipantAsync()
    {
        await using var db = _fixture.CreateDbContext();
        return await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.MatchId == MatchId)
            .ToDictionaryAsync(p => p.ParticipantId, p => p.RoleBoundItemId);
    }

    private async Task RunBackfillAsync()
    {
        var process = new MatchRoleBoundItemBackfillProcess(
            NullLogger<MatchRoleBoundItemBackfillProcess>.Instance,
            new TestDbContextFactory(_fixture),
            new BootsMetadataProvider());
        await process.RunCoreAsync(CancellationToken.None);
    }

    private sealed class BootsMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata = new Dictionary<int, ItemMetadata>
        {
            [Greaves] = new(Greaves, 1100, true, false, true, false, true, true),
            [Gluttonous] = new(Gluttonous, 1000, true, false, true, false, true, true),
            [Legendary] = new(Legendary, 3200, true, false, false, false, true, false),
        };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);
    }
}
