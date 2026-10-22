using Core.Lol.Benchmarks;
using Core.Lol.Ranking;
using Data.Aggregation;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Turns one match's end-of-game lines into form benchmark bin increments (#2076): each
/// laner's KDA, kill participation, CS/min, deaths and vision/min, binned by
/// <see cref="FormHistogram"/> and keyed by the lobby's tier — the tier the pace benchmark
/// counts the same game at (<see cref="PaceBenchmarkBuilder.ResolveLobbyTier"/>). Pure.
/// </summary>
internal static class FormBenchmarkBuilder
{
    /// <summary>What the fold reads of one participant: their team, lane and end-of-game line.</summary>
    public readonly record struct Line(
        int TeamId,
        string Position,
        int Kills,
        int Deaths,
        int Assists,
        int CreepScore,
        int VisionScore);

    /// <summary>
    /// The bins a stored match adds, or nothing when it cannot be placed: a remake, a match
    /// without a patch, or a lobby with no tracked account ranked at game time.
    /// </summary>
    public static List<FormBenchmarkKey> Build(
        PaceBenchmarkFoldClaim match,
        IReadOnlyCollection<MatchParticipant> participants,
        IReadOnlyDictionary<Guid, List<(DateTime CapturedAtUtc, string? Tier)>> tierHistoryByAccount)
    {
        if (string.IsNullOrEmpty(match.Patch))
        {
            return [];
        }

        var tier = PaceBenchmarkBuilder.ResolveLobbyTier(participants, tierHistoryByAccount, match.GameStartTimeUtc);
        if (tier is null)
        {
            return [];
        }

        var lines = participants
            .Select(participant => new Line(
                participant.TeamId,
                participant.TeamPosition,
                participant.Kills,
                participant.Deaths,
                participant.Assists,
                participant.TotalMinionsKilled + participant.NeutralMinionsKilled,
                participant.VisionScore))
            .ToList();

        return BuildAtTier(match.Patch, match.GameDurationSeconds, match.EndedInEarlySurrender, tier.Value, lines);
    }

    /// <summary>
    /// The bins one match adds once its tier is known — the low-tier sampler's entry point.
    /// <paramref name="lines"/> is the whole lobby: every line counts towards its team's kills,
    /// only those on a canonical lane are binned. A remake adds nothing.
    /// </summary>
    public static List<FormBenchmarkKey> BuildAtTier(
        string patch,
        int gameDurationSeconds,
        bool endedInEarlySurrender,
        RankTier tier,
        IReadOnlyCollection<Line> lines)
    {
        var keys = new List<FormBenchmarkKey>();
        if (ChampionCohort.IsRemake(gameDurationSeconds, endedInEarlySurrender) || gameDurationSeconds <= 0)
        {
            return keys;
        }

        var minutes = gameDurationSeconds / 60d;
        var teamKills = lines
            .GroupBy(line => line.TeamId)
            .ToDictionary(team => team.Key, team => team.Sum(line => line.Kills));

        foreach (var line in lines.Where(line => ChampionCohort.IsCanonicalPosition(line.Position)))
        {
            var kills = teamKills[line.TeamId];
            var killParticipation = kills > 0 ? (line.Kills + line.Assists) / (double)kills : 0;

            keys.Add(Key(patch, tier, line.Position, FormMetric.Kda, (line.Kills + line.Assists) / (double)Math.Max(line.Deaths, 1)));
            keys.Add(Key(patch, tier, line.Position, FormMetric.KillParticipation, killParticipation));
            keys.Add(Key(patch, tier, line.Position, FormMetric.CsPerMinute, line.CreepScore / minutes));
            keys.Add(Key(patch, tier, line.Position, FormMetric.Deaths, line.Deaths));
            keys.Add(Key(patch, tier, line.Position, FormMetric.VisionPerMinute, line.VisionScore / minutes));
        }

        return keys;
    }

    /// <summary>Sums a batch's bin increments into one count per key, the shape the upsert takes.</summary>
    public static List<FormBenchmarkCount> Count(IEnumerable<FormBenchmarkKey> keys)
        => keys
            .GroupBy(key => key)
            .Select(group => new FormBenchmarkCount(group.Key, group.LongCount()))
            .ToList();

    private static FormBenchmarkKey Key(string patch, RankTier tier, string position, FormMetric metric, double value)
        => new(patch, tier, position, metric, FormHistogram.ToBucket(metric, value));
}
