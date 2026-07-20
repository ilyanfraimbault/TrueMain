using Core.Lol.Map;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Backfills <c>match_participants.team_position</c> for already-persisted rows
/// left with an unresolved lane by upstream Riot data, using the same unambiguous
/// single-gap inference <c>RiotMatchMapper</c> applies to newly ingested matches
/// (#791). Only a full 5-player team with exactly one missing canonical lane and
/// exactly one unresolved member is touched; anything more ambiguous (several
/// gaps, a duplicated lane) is left flagged on the Data Quality panel for manual
/// review — see <see cref="TeamPositionInferrer"/>.
/// </summary>
public sealed class MatchTeamPositionCorrectionProcess(
    ILogger<MatchTeamPositionCorrectionProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory) : IIngestorProcess
{
    private static readonly int TeamSize = QueueDataQualityProfile.LanePositions.Count;

    // Bounds how many ambiguous (match, team) pairs are inspected per run. Once
    // RiotMatchMapper's ingestion-time fix stops new gaps from appearing, this
    // only ever drains the pre-existing backlog — a corrected team drops out of
    // the candidate set on the next scan, so steady state is a near-empty query.
    private const int BatchSize = 2000;

    public string Name => "MatchTeamPositionCorrection";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Empty TeamPosition is the only "unresolved" signal Riot's data carries
        // (mirrors DataQualityQueryService's own check), so this is a small,
        // selective subset of an otherwise huge table.
        var candidateTeams = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.TeamPosition == "")
            .Select(p => new { p.MatchId, p.TeamId })
            .Distinct()
            .Take(BatchSize)
            .ToListAsync(ct);

        if (candidateTeams.Count == 0)
        {
            return new { correctedParticipants = 0, inspectedTeams = 0 };
        }

        var matchIds = candidateTeams.Select(t => t.MatchId).Distinct().ToList();
        var members = await db.MatchParticipants
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new { p.Id, p.MatchId, p.TeamId, p.TeamPosition })
            .ToListAsync(ct);

        var membersByTeam = members
            .GroupBy(p => (p.MatchId, p.TeamId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group corrections by the inferred position so the writes are a handful
        // of set-based UPDATEs (one per distinct lane) instead of one per row.
        var idsByInferredPosition = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var team in candidateTeams)
        {
            if (!membersByTeam.TryGetValue((team.MatchId, team.TeamId), out var teamMembers)
                || teamMembers.Count != TeamSize)
            {
                continue;
            }

            var positions = teamMembers.Select(m => m.TeamPosition).ToList();
            if (!TeamPositionInferrer.TryInferSingleMissingPosition(positions, out var unresolvedIndex, out var inferredPosition))
            {
                continue;
            }

            if (!idsByInferredPosition.TryGetValue(inferredPosition, out var ids))
            {
                ids = [];
                idsByInferredPosition[inferredPosition] = ids;
            }
            ids.Add(teamMembers[unresolvedIndex].Id);
        }

        var corrected = 0;
        foreach (var (position, ids) in idsByInferredPosition)
        {
            corrected += await db.MatchParticipants
                .Where(p => ids.Contains(p.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.TeamPosition, position), ct);
        }

        if (corrected > 0)
        {
            logger.LogInformation(
                "Team position correction resolved {Corrected} unambiguous participant(s) across {InspectedTeams} flagged team(s).",
                corrected,
                candidateTeams.Count);
        }

        return new { correctedParticipants = corrected, inspectedTeams = candidateTeams.Count };
    }
}
