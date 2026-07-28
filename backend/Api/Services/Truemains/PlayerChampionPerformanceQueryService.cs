using Core.Lol.Performance;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Aggregates TrueMain's per-match performance score over one player's recent
/// ranked games on one champion, for the player-scoped champion page.
///
/// <para><b>A form metric, not a career one.</b> Only the most recent
/// <see cref="Window"/> games are graded: the panel answers "how is this player
/// playing this champion right now", and the bound is also what keeps the read
/// cheap — every graded match needs its ten participants, its timeline marks and
/// its kill positions, and Postgres runs these single-threaded
/// (<c>max_parallel_workers_per_gather = 0</c>).</para>
///
/// <para><b>Honest about thin samples.</b> Below <see cref="MinGames"/> graded
/// games every average is suppressed rather than published with a caveat, and
/// each component reports the number of games it was actually available in — a
/// game with no timeline coverage is left out of the laning average instead of
/// dragging it toward zero. Computed live, cached 60 s.</para>
/// </summary>
public sealed class PlayerChampionPerformanceQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IMemoryCache cache)
    : IPlayerChampionPerformanceQueryService
{
    /// <summary>
    /// Most recent ranked games on the champion the panel grades. Twenty is the
    /// same order as one page of match history, so a reader can line the average
    /// up against the games it came from.
    /// </summary>
    public const int Window = 20;

    /// <summary>
    /// Graded games below which the averages are suppressed. Matches
    /// <see cref="PlayerChampionBuildsQueryService.MinPlayerGames"/> — the same
    /// page should not call five games enough to name a build but too few to
    /// grade a performance.
    /// </summary>
    public const int MinGames = PlayerChampionBuildsQueryService.MinPlayerGames;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<PlayerChampionPerformanceResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed) || championId <= 0)
        {
            return null;
        }

        // Same name-tag resolution as the sibling player-scoped routes, so all
        // of them agree on which account a name tag means.
        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new { a.Id, a.Puuid })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var cacheKey = $"truemains:champion-performance:{account.Id}:{championId}:{patch ?? "all"}:{position ?? "all"}";
        if (cache.TryGetValue<PlayerChampionPerformanceResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = patch is null ? null : $"{patch}.%";

        // The player's own rows on the champion, newest first, bounded to the
        // window. Only the match ids are needed here — every stat is re-read
        // below with the other nine participants, so the score is computed from
        // exactly the same rows the detail page would use.
        var windowMatchIds = await (
            from p in db.MatchParticipants.AsNoTracking()
            join m in db.Matches.AsNoTracking() on p.MatchId equals m.Id
            where p.Puuid == account.Puuid
                  && p.ChampionId == championId
                  && m.QueueId == queueId
                  && (patchPrefix == null || EF.Functions.Like(m.GameVersion, patchPrefix))
                  && (position == null || p.TeamPosition == position)
            orderby m.GameStartTimeUtc descending
            select new { p.MatchId, m.GameDurationSeconds })
            .Take(Window)
            .ToListAsync(ct);

        if (windowMatchIds.Count == 0)
        {
            return Cache(cacheKey, Empty(championId, position, patch));
        }

        var matchIds = windowMatchIds.Select(m => m.MatchId).ToList();
        var durationByMatch = windowMatchIds
            .GroupBy(m => m.MatchId)
            .ToDictionary(g => g.Key, g => g.First().GameDurationSeconds);

        // Every projection below stays scalar and is shaped into its struct in
        // memory, so nothing depends on EF translating a record-struct
        // constructor inside a projection.
        var participantRows = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new
            {
                p.MatchId,
                p.Puuid,
                p.ParticipantId,
                p.TeamId,
                p.TeamPosition,
                p.Win,
                p.Kills,
                p.Deaths,
                p.Assists,
                Cs = p.TotalMinionsKilled + p.NeutralMinionsKilled,
                p.TotalDamageDealtToChampions,
                p.GoldEarned,
                p.VisionScore,
            })
            .ToListAsync(ct);

        var participants = participantRows
            .Select(p => new
            {
                p.MatchId,
                p.Puuid,
                Participant = new ScoredParticipant(
                    p.ParticipantId,
                    p.TeamId,
                    p.TeamPosition,
                    p.Win,
                    p.Kills,
                    p.Deaths,
                    p.Assists,
                    p.Cs,
                    p.TotalDamageDealtToChampions,
                    p.GoldEarned,
                    p.VisionScore),
            })
            .ToList();

        var marks = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId))
            .Select(s => new
            {
                s.MatchId,
                s.ParticipantId,
                s.IntervalMinute,
                Cs = s.MinionsKilled + s.JungleMinionsKilled,
                s.TotalGold,
                s.Xp,
            })
            .ToListAsync(ct);

        var marksByMatch = marks
            .GroupBy(r => r.MatchId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark>)g
                    .GroupBy(r => (r.ParticipantId, Minute: r.IntervalMinute))
                    .ToDictionary(
                        inner => inner.Key,
                        inner =>
                        {
                            var row = inner.First();
                            return new TimelineMark(
                                row.ParticipantId, row.IntervalMinute, row.Cs, row.TotalGold, row.Xp);
                        }));

        var killRows = await db.MatchParticipantKillPositions
            .AsNoTracking()
            .Where(k => matchIds.Contains(k.MatchId))
            .Select(k => new { k.MatchId, k.ParticipantId, k.X, k.Y })
            .ToListAsync(ct);

        var killSpotsByMatch = killRows
            .GroupBy(r => r.MatchId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<KillSpot>)g
                    .Select(r => new KillSpot(r.ParticipantId, r.X, r.Y))
                    .ToList());

        var accumulator = new ScoreAccumulator();

        foreach (var group in participants.GroupBy(p => p.MatchId))
        {
            var roster = group.Select(p => p.Participant).ToList();
            var self = group.FirstOrDefault(p => p.Puuid == account.Puuid);
            if (self is null)
            {
                // The window came from this player's own rows, so a match with no
                // self row means the participant set changed under us mid-read.
                continue;
            }

            var built = PerformanceInputs.BuildMatchInputs(
                roster,
                durationByMatch.GetValueOrDefault(group.Key),
                marksByMatch.TryGetValue(group.Key, out var mm) ? mm : PerformanceInputs.NoMarks,
                killSpotsByMatch.TryGetValue(group.Key, out var ks) ? ks : Array.Empty<KillSpot>());

            var entries = built.Select(b => new MatchPerformanceEntry
            {
                ParticipantId = b.Participant.ParticipantId,
                Win = b.Participant.Win,
                Score = PerformanceScore.Compute(b.Input),
                Kills = b.Participant.Kills,
                Deaths = b.Participant.Deaths,
                Assists = b.Participant.Assists,
            }).ToList();

            var placements = MatchPerformanceRanker.Rank(entries);
            var selfInput = built.First(b => b.Participant.ParticipantId == self.Participant.ParticipantId).Input;
            var selfPlacement = placements[self.Participant.ParticipantId];

            accumulator.Add(
                PerformanceScore.Explain(selfInput),
                topOfTeam: selfPlacement.IsMvp || selfPlacement.IsAce);
        }

        return Cache(cacheKey, accumulator.ToResponse(championId, position, patch));
    }

    private PlayerChampionPerformanceResponse Cache(
        string cacheKey,
        PlayerChampionPerformanceResponse response)
    {
        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1,
        });

        return response;
    }

    private static PlayerChampionPerformanceResponse Empty(int championId, string? position, string? patch)
        => new()
        {
            ChampionId = championId,
            Position = position,
            Patch = patch,
            Games = 0,
            MinGames = MinGames,
            Window = Window,
        };

    /// <summary>
    /// Running totals over the window. Each component keeps its own denominator
    /// so a dropped component lowers its sample rather than its average.
    /// </summary>
    private sealed class ScoreAccumulator
    {
        private static readonly PerformanceComponentKind[] Kinds = Enum.GetValues<PerformanceComponentKind>();

        private readonly double[] _componentTotals = new double[Kinds.Length];
        private readonly int[] _componentGames = new int[Kinds.Length];
        private readonly double[] _weightTotals = new double[Kinds.Length];

        private int _games;
        private int _scoreTotal;
        private int _best;
        private int _worst = int.MaxValue;
        private int _topOfTeam;

        public void Add(PerformanceScoreBreakdown breakdown, bool topOfTeam)
        {
            _games++;
            _scoreTotal += breakdown.Score;
            _best = Math.Max(_best, breakdown.Score);
            _worst = Math.Min(_worst, breakdown.Score);
            if (topOfTeam)
            {
                _topOfTeam++;
            }

            for (var i = 0; i < Kinds.Length; i++)
            {
                var component = breakdown.Components[i];

                // The nominal weight is a property of the role, so it averages
                // over every game — including one where the component itself was
                // dropped. Only the grade needs the narrower denominator.
                _weightTotals[i] += component.Weight;
                if (component.Value is { } value)
                {
                    _componentTotals[i] += value;
                    _componentGames[i]++;
                }
            }
        }

        public PlayerChampionPerformanceResponse ToResponse(int championId, string? position, string? patch)
        {
            if (_games < MinGames)
            {
                return new PlayerChampionPerformanceResponse
                {
                    ChampionId = championId,
                    Position = position,
                    Patch = patch,
                    Games = _games,
                    MinGames = MinGames,
                    Window = Window,
                };
            }

            var components = new PlayerChampionPerformanceComponent[Kinds.Length];
            for (var i = 0; i < Kinds.Length; i++)
            {
                components[i] = new PlayerChampionPerformanceComponent
                {
                    Kind = Kinds[i].ToString(),
                    Weight = _weightTotals[i] / _games,
                    Value = _componentGames[i] == 0
                        ? null
                        : _componentTotals[i] / _componentGames[i],
                    Games = _componentGames[i],
                };
            }

            return new PlayerChampionPerformanceResponse
            {
                ChampionId = championId,
                Position = position,
                Patch = patch,
                Games = _games,
                MinGames = MinGames,
                Window = Window,
                AverageScore = (double)_scoreTotal / _games,
                BestScore = _best,
                WorstScore = _worst,
                TopOfTeamRate = (double)_topOfTeam / _games,
                Components = components,
            };
        }
    }
}
