using System.Diagnostics.CodeAnalysis;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Matchups;

/// <summary>
/// A champion's slice narrowed to one lane opponent — the scope of the matchup build page.
/// Built through <see cref="TryCreate"/>, which owns the two rules the matchup aggregate
/// imposes, so no caller restates them.
/// </summary>
public sealed record LaneMatchupScope
{
    private LaneMatchupScope(ChampionScope champion, string lane, int opponentChampionId)
    {
        Champion = champion;
        Lane = lane;
        OpponentChampionId = opponentChampionId;
    }

    /// <summary>The champion side: its lane, patch and elo filter.</summary>
    public ChampionScope Champion { get; }

    /// <summary>The lane both sides share.</summary>
    public string Lane { get; }

    public int OpponentChampionId { get; }

    /// <summary>
    /// Narrows <paramref name="champion"/> to <paramref name="opponentChampionId"/>, or names why
    /// it cannot be: "vs Darius" is only meaningful in a lane, since the self-join matches both
    /// sides on it; and the matchup aggregate's champion side is mains-only (#1087), so the
    /// truemains population is the only one it has. Both are rejected rather than answered —
    /// global data under a matchup label, or mains-only rows under an everyone label, would be
    /// a fabricated answer, not a lenient one.
    /// </summary>
    public static bool TryCreate(
        ChampionScope champion,
        int opponentChampionId,
        [NotNullWhen(true)] out LaneMatchupScope? matchup,
        [NotNullWhen(false)] out string? rejection)
    {
        matchup = null;
        if (champion.Lane is not { } lane)
        {
            rejection = "A matchup requires a position: pass ?position= alongside ?opponentChampionId=.";
            return false;
        }

        if (champion.Population != ChampionPopulation.Truemains)
        {
            rejection = "Matchups are aggregated over truemains only: ?truemainsOnly=false cannot be combined "
                + "with ?opponentChampionId=.";
            return false;
        }

        matchup = new LaneMatchupScope(champion, lane, opponentChampionId);
        rejection = null;
        return true;
    }

    /// <summary>The scope's contribution to a champion-read cache key.</summary>
    public string CacheToken() => $"{Champion.CacheToken()}:vs:{OpponentChampionId}";
}
