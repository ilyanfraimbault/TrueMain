namespace Core.Lol.Map;

/// <summary>
/// Infers a single missing lane assignment for a lane-queue team of five: when
/// exactly one of the five canonical <see cref="QueueDataQualityProfile.LanePositions"/>
/// is absent from the team and exactly one member's <c>TeamPosition</c> is blank or
/// unrecognised, the pairing is unambiguous — that member must be the missing lane.
/// Used both to self-heal newly-ingested matches (<c>RiotMatchMapper</c>) and to
/// backfill already-persisted ones (<c>MatchTeamPositionCorrectionProcess</c>).
/// Any other shape (several gaps, a duplicated lane, no gap at all) is left
/// untouched rather than guessed.
/// </summary>
public static class TeamPositionInferrer
{
    /// <summary>
    /// Attempts to resolve the single unresolved member of a team to its missing
    /// lane. <paramref name="teamPositions"/> must be the raw <c>TeamPosition</c>
    /// of every member of one team (order doesn't matter). Returns false — with the
    /// out params left at their defaults — unless there is exactly one member whose
    /// position isn't one of the five canonical lanes AND exactly one canonical
    /// lane is missing from the rest.
    /// </summary>
    public static bool TryInferSingleMissingPosition(
        IReadOnlyList<string> teamPositions,
        out int unresolvedIndex,
        out string inferredPosition)
    {
        unresolvedIndex = -1;
        inferredPosition = string.Empty;

        var unresolvedIndices = new List<int>();
        var filledPositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < teamPositions.Count; i++)
        {
            var position = teamPositions[i];
            if (string.IsNullOrEmpty(position)
                || !QueueDataQualityProfile.LanePositions.Contains(position, StringComparer.OrdinalIgnoreCase))
            {
                unresolvedIndices.Add(i);
            }
            else
            {
                filledPositions.Add(position);
            }
        }

        if (unresolvedIndices.Count != 1)
        {
            return false;
        }

        var missingPositions = QueueDataQualityProfile.LanePositions
            .Where(position => !filledPositions.Contains(position))
            .ToList();

        if (missingPositions.Count != 1)
        {
            return false;
        }

        unresolvedIndex = unresolvedIndices[0];
        inferredPosition = missingPositions[0];
        return true;
    }
}
