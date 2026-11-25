namespace Core.Lol.Map;

/// <summary>
/// Infers a single missing lane assignment for a lane-queue team of five: when
/// exactly one of the five canonical <see cref="LanePositions.All"/>
/// is absent from the team and exactly one member's <c>TeamPosition</c> is blank or
/// unrecognised, the pairing is unambiguous — that member must be the missing lane.
/// Applied to every match at ingestion (<c>RiotMatchMapper</c>); the backfill step that
/// corrected matches stored before it was retired with #2481.
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
                || !LanePositions.All.Contains(position, StringComparer.OrdinalIgnoreCase))
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

        var missingPositions = LanePositions.All
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
