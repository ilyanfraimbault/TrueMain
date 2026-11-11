using Core.Lol.Patches;
using Core.Lol.Spells;
using Data.Entities;
using Data.BuildFacts;

namespace Ingestor.Processes.Components.Retention;

/// <summary>One chosen perk of a seat's rune page, as <c>participant_perk_selections</c> × its catalogue holds it.</summary>
public sealed record TruncatedPerkSelection(string StyleDescription, int SelectionIndex, int PerkId);

/// <summary>
/// Truncates one seat of a match retention is about to delete into a
/// <see cref="TruncatedMatchSeat"/> (#2368): the build facts, resolved by the same primitives
/// every live build read uses, and the end-of-game line. Pure, so the rule is tested without
/// a database.
/// </summary>
public static class TruncatedSeatBuilder
{
    private const string PrimaryStyle = "primaryStyle";
    private const string SubStyle = "subStyle";

    /// <summary>The truncated seat of <paramref name="participant"/>.</summary>
    /// <param name="match">The match the seat belongs to.</param>
    /// <param name="participant">The seat, with its item and skill events.</param>
    /// <param name="perks">The seat's chosen perks; empty when none were stored.</param>
    /// <param name="itemMetadata">
    /// The item data of the match's patch, or null when it is unavailable — the item facts then
    /// stay empty rather than guessed, as the live reads leave them.
    /// </param>
    public static TruncatedMatchSeat Build(
        Match match,
        MatchParticipant participant,
        IReadOnlyList<TruncatedPerkSelection> perks,
        IReadOnlyDictionary<int, ItemMetadata>? itemMetadata)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(perks);

        var spells = new SummonerSpellPair(participant.Summoner1Id, participant.Summoner2Id).Canonical();
        var seat = new TruncatedMatchSeat
        {
            MatchId = match.Id,
            ParticipantId = participant.ParticipantId,
            PlatformId = match.PlatformId,
            QueueId = match.QueueId,
            // The stored generated column, or the same major.minor read off the version when
            // the match was never round-tripped through the database.
            Patch = match.Patch ?? (PatchVersion.TryParse(match.GameVersion, out var version) ? version.ToMajorMinor() : string.Empty),
            GameStartTimeUtc = match.GameStartTimeUtc,
            GameDurationSeconds = match.GameDurationSeconds,
            EloBracket = participant.EloBracket,
            TeamId = participant.TeamId,
            Win = participant.Win,
            ChampionId = participant.ChampionId,
            TeamPosition = participant.TeamPosition,
            RiotAccountId = participant.RiotAccountId,
            Summoner1Id = spells.Spell1Id,
            Summoner2Id = spells.Spell2Id,
            PrimaryStyleId = participant.PrimaryStyleId,
            SubStyleId = participant.SubStyleId,
            RunePerkIds = RunePerks(perks),
            PerksOffense = participant.PerksOffense,
            PerksFlex = participant.PerksFlex,
            PerksDefense = participant.PerksDefense,
            SkillOrderKey = SkillOrderBuilder.Build(participant.SkillEvents),
            Kills = participant.Kills,
            Deaths = participant.Deaths,
            Assists = participant.Assists,
        };

        if (itemMetadata is null)
        {
            return seat;
        }

        var finalItems = FinalInventory.Of(
            participant.Item0, participant.Item1, participant.Item2, participant.Item3,
            participant.Item4, participant.Item5, participant.RoleBoundItemId);
        var starter = StarterItemAnalyzer.Analyze(participant.ItemEvents, finalItems, itemMetadata);
        var buildItems = FinalBuildResolver.Resolve(participant.ItemEvents, finalItems, starter.Items, itemMetadata);

        seat.StarterItemIds = [.. starter.Items];
        seat.BuildItemIds = buildItems;
        seat.BuildItemMinutes = [.. buildItems.Select(itemId => CompletionMinute(participant.ItemEvents, itemId))];
        seat.BootsItemId = BootsResolver.Resolve(participant.ItemEvents, finalItems, starter.Items, itemMetadata);
        return seat;
    }

    /// <summary>
    /// The six chosen perks, primary tree first in selection order (the keystone first), then
    /// the secondary tree; empty unless both trees are complete — the live rune-page read
    /// discards an incomplete page the same way.
    /// </summary>
    public static int[] RunePerks(IReadOnlyList<TruncatedPerkSelection> perks)
    {
        ArgumentNullException.ThrowIfNull(perks);

        var primary = Tree(perks, PrimaryStyle);
        var secondary = Tree(perks, SubStyle);
        return primary.Count < 4 || secondary.Count < 2
            ? []
            : [.. primary.Take(4), .. secondary.Take(2)];
    }

    /// <summary>
    /// The minute an item was completed: its first purchase, rounded down; -1 when the
    /// timeline holds no purchase of it (an inventory transform, a legacy row).
    /// </summary>
    public static int CompletionMinute(IReadOnlyList<ItemEvent> events, int itemId)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var itemEvent in events)
        {
            if (itemEvent.ItemId == itemId
                && string.Equals(itemEvent.EventType, ItemEventTypes.Purchased, StringComparison.OrdinalIgnoreCase))
            {
                return itemEvent.TimestampMs / 60_000;
            }
        }

        return -1;
    }

    private static List<int> Tree(IReadOnlyList<TruncatedPerkSelection> perks, string style)
        => [.. perks
            .Where(perk => string.Equals(perk.StyleDescription, style, StringComparison.OrdinalIgnoreCase))
            .OrderBy(perk => perk.SelectionIndex)
            .Select(perk => perk.PerkId)];
}
