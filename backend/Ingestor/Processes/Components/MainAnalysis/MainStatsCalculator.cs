using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.Coverage;

namespace Ingestor.Processes.Components.MainAnalysis;

public sealed class MainStatsCalculator : IMainStatsCalculator
{
    public List<MainChampionStat> Calculate(
        string platformId,
        string puuid,
        IReadOnlyCollection<ParticipantRow> participantRows,
        MainAnalysisOptions options,
        ChampionCoverageSnapshot coverage,
        DateTime calculatedAtUtc)
    {
        var validParticipants = participantRows
            .Where(row => IsValidTeamPosition(row.TeamPosition))
            .Select(row => new ParticipantRow(row.ChampionId, NormalizePosition(row.TeamPosition)))
            .ToList();

        var totalMatches = validParticipants.Count;
        if (totalMatches == 0)
        {
            return [];
        }

        return validParticipants
            .GroupBy(row => row.ChampionId)
            .Select(group => BuildStat(platformId, puuid, group, totalMatches, options, coverage, calculatedAtUtc))
            .ToList();
    }

    private static MainChampionStat BuildStat(
        string platformId,
        string puuid,
        IGrouping<int, ParticipantRow> group,
        int totalMatches,
        MainAnalysisOptions options,
        ChampionCoverageSnapshot coverage,
        DateTime calculatedAtUtc)
    {
        var championMatches = group.Count();
        var playRate = (double)championMatches / totalMatches;
        var eligibleForClassification = totalMatches >= options.MinMatchesToEvaluate;

        var positions = group
            .GroupBy(row => row.TeamPosition)
            .Select(positionGroup =>
            {
                var games = positionGroup.Count();
                return new PositionStat
                {
                    Position = positionGroup.Key,
                    Games = games,
                    Rate = championMatches == 0 ? 0 : (double)games / championMatches
                };
            })
            .OrderByDescending(position => position.Games)
            .ToList();

        var primaryPosition = positions.Count > 0 ? positions[0].Position : string.Empty;
        var mainThreshold = ResolveMainThreshold(group.Key, options, coverage);
        var isMain = eligibleForClassification && playRate >= mainThreshold;
        var isOtp = isMain && playRate >= options.OtpPlayRateThreshold;
        var isExtendedSample = isMain && playRate < options.PlayRateThreshold;

        return new MainChampionStat
        {
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = group.Key,
            TotalMatches = totalMatches,
            ChampionMatches = championMatches,
            PlayRate = playRate,
            IsMain = isMain,
            IsOtp = isOtp,
            IsExtendedSample = isExtendedSample,
            PrimaryPosition = primaryPosition,
            PositionBreakdown = positions,
            CalculatedAtUtc = calculatedAtUtc
        };
    }

    /// <summary>
    /// Per-champion main threshold, interpolated from the coverage deficit:
    /// covered champions keep <see cref="MainAnalysisOptions.PlayRateThreshold"/>,
    /// maximally under-covered champions drop to <see cref="MainAnalysisOptions.PlayRateFloor"/>.
    /// </summary>
    private static double ResolveMainThreshold(
        int championId,
        MainAnalysisOptions options,
        ChampionCoverageSnapshot coverage)
    {
        // Defensive: startup validation guarantees PlayRateFloor <= PlayRateThreshold, so this
        // Min is a no-op in practice. It guards against an inverted (tightening) threshold if the
        // validator is ever bypassed, mirroring the weightSum guard in ScoringProcess.
        var floor = Math.Min(options.PlayRateFloor, options.PlayRateThreshold);
        var deficit = coverage.Deficit(championId);
        return options.PlayRateThreshold - (options.PlayRateThreshold - floor) * deficit;
    }

    private static bool IsValidTeamPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return false;
        }

        var normalized = position.Trim();
        return !normalized.Equals("NONE", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePosition(string position)
        => position.Trim().ToUpperInvariant();
}
