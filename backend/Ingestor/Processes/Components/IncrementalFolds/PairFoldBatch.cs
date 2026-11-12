using Core.Lol.Patches;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.IncrementalFolds;

/// <summary>One seat of a match, as the champion-pair folds read it.</summary>
internal sealed record FoldParticipant(
    string MatchId,
    int ParticipantId,
    int ChampionId,
    int TeamId,
    string TeamPosition,
    string EloBracket,
    bool Win);

/// <summary>
/// What every fold that pairs a champion with the other seats of its game reads for one batch —
/// the matchup (#606), synergy (#922) and opponent (#1713) folds: each match's patch, the cohort
/// that may sit on the queried side, and the match's seats on a canonical lane.
/// </summary>
/// <remarks>
/// Every seat is loaded, cohort or not — an off-cohort player is still somebody's lane opponent,
/// partner or enemy — and membership is tested per row against <see cref="Cohort"/>. A seat with
/// an empty or garbage <c>TeamPosition</c> cannot be placed in a composition, so it is excluded on
/// both sides; it is the same canonical set the cohort tests, so the two sides cannot drift apart.
/// The patch is the game version normalised, as the live reads of these panels have always keyed it.
/// </remarks>
internal sealed record PairFoldBatch(
    IReadOnlyDictionary<string, string> PatchByMatch,
    ChampionCohortSnapshot Cohort,
    IReadOnlyDictionary<string, List<FoldParticipant>> ParticipantsByMatch)
{
    /// <summary>Loads the batch of <paramref name="matchIds"/>.</summary>
    public static async Task<PairFoldBatch> LoadAsync(TrueMainDbContext db, List<string> matchIds, CancellationToken ct)
    {
        var patchByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion })
            .ToDictionaryAsync(m => m.Id, m => PatchVersion.Normalize(m.GameVersion), ct);

        var cohort = await ChampionCohort.LoadAsync(db, matchIds, ct);

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && ChampionCohort.CanonicalPositions.Contains(p.TeamPosition))
            .Select(p => new FoldParticipant(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.EloBracket,
                p.Win))
            .ToListAsync(ct);

        return new PairFoldBatch(
            patchByMatch,
            cohort,
            participants.GroupBy(p => p.MatchId).ToDictionary(g => g.Key, g => g.ToList()));
    }

    /// <summary>Each match of the batch with a known patch, and its seats.</summary>
    public IEnumerable<(string MatchId, string Patch, List<FoldParticipant> Seats)> Matches()
    {
        foreach (var (matchId, seats) in ParticipantsByMatch)
        {
            var patch = PatchByMatch.GetValueOrDefault(matchId);
            if (!string.IsNullOrEmpty(patch))
            {
                yield return (matchId, patch, seats);
            }
        }
    }
}
