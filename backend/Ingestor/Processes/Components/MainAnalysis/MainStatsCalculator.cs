using Core.Options;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Processes.Components.MainAnalysis;

public sealed class MainStatsCalculator : IMainStatsCalculator
{
    public List<MainChampionStat> Calculate(
        string platformId,
        string puuid,
        IReadOnlyCollection<ParticipantRow> participantRows,
        MainAnalysisOptions options,
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
            .Select(group => BuildStat(platformId, puuid, group, totalMatches, options, calculatedAtUtc))
            .ToList();
    }

    private static MainChampionStat BuildStat(
        string platformId,
        string puuid,
        IGrouping<int, ParticipantRow> group,
        int totalMatches,
        MainAnalysisOptions options,
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
        var isMain = eligibleForClassification && playRate >= options.PlayRateThreshold;
        var isOtp = isMain && playRate >= options.OtpPlayRateThreshold;

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
            PrimaryPosition = primaryPosition,
            PositionBreakdown = positions,
            CalculatedAtUtc = calculatedAtUtc
        };
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
