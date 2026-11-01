using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Ingestor.Processes.Components.Retention;

namespace TrueMain.UnitTests;

/// <summary>
/// What match retention keeps of a deleted match (#2368, #2358): the seat's facts, resolved as
/// the live reads resolve them, and the two-tier window they are kept for.
/// </summary>
public sealed class TruncatedMatchSeatTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ASeatKeepsItsLineAndCanonicalSpells_AndNoItemFactsWithoutItemData()
    {
        var seat = TruncatedSeatBuilder.Build(Match(), Participant(), [], itemMetadata: null, Now);

        seat.MatchId.Should().Be("EUW1_1");
        seat.Patch.Should().Be("16.21");
        (seat.Summoner1Id, seat.Summoner2Id).Should().Be((4, 14), "the pair is stored canonical, lower id first");
        (seat.Kills, seat.Deaths, seat.Assists).Should().Be((7, 2, 9));
        seat.SkillOrderKey.Should().NotBeNull();
        seat.BuildItemIds.Should().BeEmpty("no item data: the item facts abstain rather than guess");
        seat.RunePerkIds.Should().BeEmpty();
        seat.TruncatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void TheRunePageIsTheSixChosenPerks_PrimaryFirst_OrEmptyWhenIncomplete()
    {
        TruncatedPerkSelection Perk(string style, int index, int perk) => new(style, index, perk);
        var page = new[]
        {
            Perk("subStyle", 1, 8234), Perk("primaryStyle", 2, 8126), Perk("primaryStyle", 0, 8112),
            Perk("subStyle", 0, 8226), Perk("primaryStyle", 3, 8106), Perk("primaryStyle", 1, 8139),
        };

        TruncatedSeatBuilder.RunePerks(page).Should().Equal(8112, 8139, 8126, 8106, 8226, 8234);
        TruncatedSeatBuilder.RunePerks(page[..4]).Should().BeEmpty();
    }

    [Fact]
    public void AnItemIsCompletedAtTheMinuteOfItsFirstPurchase()
    {
        List<ItemEvent> events =
        [
            new() { TimestampMs = 400_000, EventType = "ITEM_PURCHASED", ItemId = 1058 },
            new() { TimestampMs = 845_000, EventType = "ITEM_PURCHASED", ItemId = 6655 },
            new() { TimestampMs = 900_000, EventType = "ITEM_SOLD", ItemId = 6655 },
            new() { TimestampMs = 1_500_000, EventType = "ITEM_PURCHASED", ItemId = 6655 },
        ];

        TruncatedSeatBuilder.CompletionMinute(events, 6655).Should().Be(14);
        TruncatedSeatBuilder.CompletionMinute(events, 3089).Should().Be(-1);
    }

    [Fact]
    public void EverySeatStaysInTheWindow_ThenOnlyTrackedOnes_UntilTheSeasonEnds()
    {
        var (outsideWindow, pastSeasons) = TruncatedMatchRetention.Tiers(
            ["16.9", "16.19", "16.20", "16.21", "16.18", "15.24", "junk"], retainedPatchCount: 4);

        outsideWindow.Should().BeEquivalentTo(["16.9", "15.24", "junk"], "16.9 sorts below 16.18 as a version");
        pastSeasons.Should().BeEquivalentTo(["15.24", "junk"]);
    }

    [Fact]
    public void NoStoredPatchMeansNothingToPurge()
    {
        var (outsideWindow, pastSeasons) = TruncatedMatchRetention.Tiers([], retainedPatchCount: 4);

        outsideWindow.Should().BeEmpty();
        pastSeasons.Should().BeEmpty();
    }

    private static Match Match() => new()
    {
        Id = "EUW1_1",
        PlatformId = "EUW1",
        QueueId = 420,
        GameVersion = "16.21.1",
        GameStartTimeUtc = Now.AddDays(-40),
        GameDurationSeconds = 1800,
    };

    private static MatchParticipant Participant() => new()
    {
        MatchId = "EUW1_1",
        ParticipantId = 3,
        Puuid = "puuid-3",
        ChampionId = 103,
        TeamId = 100,
        TeamPosition = "MIDDLE",
        Win = true,
        Kills = 7,
        Deaths = 2,
        Assists = 9,
        Summoner1Id = 14,
        Summoner2Id = 4,
        PrimaryStyleId = 8100,
        SubStyleId = 8200,
        EloBracket = "MASTER",
    };
}
