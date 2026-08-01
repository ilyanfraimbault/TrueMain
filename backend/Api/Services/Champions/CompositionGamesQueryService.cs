using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Truemains;

namespace TrueMain.Services.Champions;

/// <summary>
/// Provenance hydration for the composition recommendation (#940): turns the
/// selection's refs into the same collapsed row the match-history feed renders
/// (via the shared <see cref="MatchSummaryHydrator"/>) and attaches the pilot's
/// Riot identity. Deliberately fed a page of refs, never the whole top-K: the
/// hydration grades every participant of every match, so its cost scales with
/// the rows actually shown.
/// </summary>
public sealed class CompositionGamesQueryService(
    TrueMainDbContext db,
    MatchSummaryHydrator hydrator)
    : ICompositionGamesQueryService
{
    public async Task<IReadOnlyList<CompositionGameReadModel>> HydrateAsync(
        IReadOnlyList<CompositionMatchRef> matches,
        CancellationToken ct)
    {
        if (matches.Count == 0)
        {
            return Array.Empty<CompositionGameReadModel>();
        }

        var keys = matches
            .Select(m => new MatchSummaryKey(m.MatchId, m.ParticipantId))
            .ToList();

        var summaries = await hydrator.HydrateAsync(keys, ct);

        // Pilot identity by puuid. The selection scans the full participant
        // pool (harvested rows included), so a puuid without a riot_accounts
        // row is expected, not an anomaly — those games stay anonymous. So is
        // a row inserted by summoner-v4 before account-v1 filled the Riot ID:
        // an empty game name is no identity, and reads better as anonymous
        // than as a bare "#TAG".
        var puuids = matches.Select(m => m.Puuid).Distinct().ToList();
        var pilotsByPuuid = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => puuids.Contains(a.Puuid) && a.GameName != "")
            .Select(a => new { a.Puuid, a.GameName, a.TagLine, a.ProfileIconId })
            .ToDictionaryAsync(
                a => a.Puuid,
                a => new CompositionGamePilotReadModel
                {
                    GameName = a.GameName,
                    TagLine = a.TagLine,
                    ProfileIconId = a.ProfileIconId,
                },
                StringComparer.Ordinal,
                ct);

        var games = new List<CompositionGameReadModel>(matches.Count);
        foreach (var match in matches)
        {
            if (!summaries.TryGetValue(new MatchSummaryKey(match.MatchId, match.ParticipantId), out var summary))
            {
                continue;
            }

            games.Add(new CompositionGameReadModel
            {
                Score = match.Score,
                IsTruemain = match.IsTruemain,
                Pilot = pilotsByPuuid.TryGetValue(match.Puuid, out var pilot) ? pilot : null,
                Match = summary,
            });
        }

        return games;
    }
}
