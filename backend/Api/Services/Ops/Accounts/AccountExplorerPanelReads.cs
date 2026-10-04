using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Accounts;

/// <summary>
/// The per-panel Postgres reads of the account explorer (#1032), one per stage
/// the account can stall in once it has a <c>riot_accounts</c> row: the candidate
/// funnel, the main-champion rows, the game counts and the rank history. Each one
/// is keyed on the already-resolved account, so none of them re-derives which
/// account a Riot ID names — that stays with <see cref="AccountExplorerQueryService"/>.
/// </summary>
internal sealed class AccountExplorerPanelReads(TrueMainDbContext db)
{
    /// <summary>
    /// How many rank snapshots to return. One row per UTC day at most, so this is
    /// roughly a season's worth of movement — enough to see the shape without
    /// making the payload a time series.
    /// </summary>
    internal const int RankSnapshotCap = 50;

    private const string DeactivationReasonNote =
        "MainActivity records only the boolean — there is no retirement-reason column. "
        + "It writes IsActive = false in two cases it cannot tell apart afterwards: the champion's "
        + "mastery lastPlayTime was older than the configured inactivity window, or Riot returned no "
        + "mastery entry for that champion at all.";

    internal async Task<IReadOnlyList<AccountExplorerCandidateReadModel>> ReadCandidatesAsync(
        RiotAccount account,
        CancellationToken ct)
        => await db.MainCandidates
            .AsNoTracking()
            .Where(c => c.PlatformId == account.PlatformId && c.Puuid == account.Puuid)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.ChampionId)
            .Select(c => new AccountExplorerCandidateReadModel
            {
                Id = c.Id,
                ChampionId = c.ChampionId,
                Status = c.Status.ToString(),
                Source = c.Source.ToString(),
                Score = c.Score,
                ScoreInputs = new AccountExplorerCandidateScoreInputsReadModel
                {
                    LastPlayTimeUtc = c.LastPlayTimeUtc,
                    ChampionRankInMasteryTop = c.ChampionRankInMasteryTop,
                    ChampionPoints = c.ChampionPoints,
                    ObservedGames = c.ObservedGames,
                    ObservedWins = c.ObservedWins
                },
                DiscoveredAtUtc = c.DiscoveredAtUtc,
                ScoredAtUtc = c.ScoredAtUtc,
                ValidatedAtUtc = c.ValidatedAtUtc
            })
            .ToListAsync(ct);

    internal async Task<IReadOnlyList<AccountExplorerMainRowReadModel>> ReadMainRowsAsync(
        RiotAccount account,
        CancellationToken ct)
    {
        var rows = await db.MainChampionStats
            .AsNoTracking()
            .Where(m => m.PlatformId == account.PlatformId && m.Puuid == account.Puuid)
            .OrderByDescending(m => m.PlayRate)
            .ThenBy(m => m.ChampionId)
            .ToListAsync(ct);

        return rows
            .Select(m => new AccountExplorerMainRowReadModel
            {
                ChampionId = m.ChampionId,
                TotalMatches = m.TotalMatches,
                ChampionMatches = m.ChampionMatches,
                PlayRate = m.PlayRate,
                IsMain = m.IsMain,
                IsOtp = m.IsOtp,
                IsExtendedSample = m.IsExtendedSample,
                IsActive = m.IsActive,
                PrimaryPosition = m.PrimaryPosition,
                PositionBreakdown = m.PositionBreakdown
                    .Select(p => new AccountExplorerPositionStatReadModel
                    {
                        Position = p.Position,
                        Games = p.Games,
                        Rate = p.Rate
                    })
                    .ToList(),
                CalculatedAtUtc = m.CalculatedAtUtc,
                // MainAnalysis stamps the account even when its thin-sample guard
                // makes it decline to rewrite the rows, so a run newer than the row
                // means "looked, refused" rather than "never looked".
                AnalysisSkipped = account.LastMainCalcAtUtc is not null
                                  && account.LastMainCalcAtUtc > m.CalculatedAtUtc,
                Deactivation = m.IsActive
                    ? null
                    : new AccountExplorerDeactivationReadModel
                    {
                        ConfirmedByActivityCheckAtUtc = account.LastActivityCheckAtUtc,
                        ReasonKnown = false,
                        ReasonNote = DeactivationReasonNote
                    }
            })
            .ToList();
    }

    /// <summary>
    /// The three game counts that exist for an account, each with the window it
    /// was measured over. They are not three views of one number: live participant
    /// rows cover every champion but are deleted by retention, the frozen
    /// aggregates survive forever but only ever folded main champions, and the
    /// analysis sample is capped by <c>MainAnalysis:MatchesToConsider</c>.
    /// </summary>
    internal async Task<AccountExplorerMatchesIngestedReadModel> ReadMatchesIngestedAsync(
        RiotAccount account,
        IReadOnlyList<AccountExplorerMainRowReadModel> mainRows,
        CancellationToken ct)
    {
        // Puuid alone, deliberately: it is globally unique (unlike the
        // (GameName, TagLine, PlatformId) triple the account was resolved by), and
        // a game played before a region transfer still belongs to this account
        // even though its PlatformId no longer matches the account's current one.
        var gameStarts =
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            where participant.Puuid == account.Puuid
            select match.GameStartTimeUtc;

        var liveCount = await gameStarts.LongCountAsync(ct);
        DateTime? oldestRetained = null;
        DateTime? newestRetained = null;
        if (liveCount > 0)
        {
            oldestRetained = await gameStarts.MinAsync(ct);
            newestRetained = await gameStarts.MaxAsync(ct);
        }

        var scopes = db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(s => s.RiotAccountId == account.Id)
            // Mains only (#1346). The explorer's career figures describe the
            // account as the product does — its main champions — so they must
            // not start counting the non-main scopes the aggregate now holds.
            .Where(s => s.IsMain);

        var careerGames = await scopes.SumAsync(s => (long)s.Games, ct);
        var patchCount = await scopes.Select(s => s.GameVersion).Distinct().CountAsync(ct);
        var oldestAggregated = await scopes.MinAsync(s => (DateTime?)s.LastGameStartTimeUtc, ct);

        var (pruned, prunedNote) = AccountExplorerVerdict.EvaluatePruning(
            liveCount, careerGames, patchCount, oldestRetained, oldestAggregated);

        return new AccountExplorerMatchesIngestedReadModel
        {
            LiveParticipantCount = liveCount,
            OldestRetainedGameStartUtc = oldestRetained,
            NewestRetainedGameStartUtc = newestRetained,
            CareerGamesFromAggregates = careerGames,
            AggregatedPatchCount = patchCount,
            OldestAggregatedGameStartUtc = oldestAggregated,
            // Every row of a pass shares its TotalMatches, so the freshest row's
            // value is the pass's sample size.
            LastAnalysisSampleSize = mainRows.Count == 0
                ? null
                : mainRows.Max(m => m.TotalMatches),
            Pruned = pruned,
            PrunedNote = prunedNote
        };
    }

    internal async Task<IReadOnlyList<AccountExplorerRankSnapshotReadModel>> ReadRankSnapshotsAsync(
        Guid riotAccountId,
        CancellationToken ct)
        => await db.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == riotAccountId)
            .OrderByDescending(s => s.CapturedAtUtc)
            .Take(RankSnapshotCap)
            .Select(s => new AccountExplorerRankSnapshotReadModel
            {
                CapturedAtUtc = s.CapturedAtUtc,
                Tier = s.Tier,
                Division = s.Division,
                LeaguePoints = s.LeaguePoints,
                Wins = s.Wins,
                Losses = s.Losses
            })
            .ToListAsync(ct);
}
