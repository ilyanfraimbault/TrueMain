using Core.Options;
using Data;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Traces one Riot ID through the pipeline for the admin account explorer
/// (#1032). The question it answers — "why does this player not show up on the
/// site?" — has a different answer in a different table depending on where the
/// account stalled, so this service resolves the account once and then reads each
/// stage beside it: identity + refresh state, the ingest lease, the candidate
/// funnel, the main-champion rows, and the rank history.
/// <para>
/// Two rules run through the whole file. <strong>Nothing is inferred from an
/// absent row without saying so</strong> — every "no" carries the sentence that
/// explains it. And <strong>no verdict is derived from configuration this
/// assembly cannot see</strong>: the claim lease, the inactivity window and the
/// retained patch count all live in the Ingestor, so this read reports measured
/// ages and measured bounds and lets the operator judge.
/// </para>
/// </summary>
public sealed class AccountExplorerQueryService(
    TrueMainDbContext db,
    ISeedRequestStore seedRequestStore,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    TimeProvider timeProvider) : IAccountExplorerQueryService
{
    /// <summary>
    /// How many rank snapshots to return. One row per UTC day at most, so this is
    /// roughly a season's worth of movement — enough to see the shape without
    /// making the payload a time series.
    /// </summary>
    internal const int RankSnapshotCap = 50;

    /// <summary>
    /// How many same-Riot-ID accounts to surface. Collisions are rare (a Riot ID
    /// is unique within a routing region, so this only fires across regions or
    /// after a recycle); the cap exists so a pathological row set cannot balloon
    /// the payload.
    /// </summary>
    private const int AccountCollisionCap = 10;

    /// <summary>
    /// How many seed requests to scan when resolving a Riot ID that has no account
    /// row. The store's search is a contains-match on name or tag, so the exact
    /// pair is filtered in memory afterwards.
    /// </summary>
    private const int SeedRequestScanLimit = 50;

    private const string DeactivationReasonNote =
        "MainActivity records only the boolean — there is no retirement-reason column. "
        + "It writes IsActive = false in two cases it cannot tell apart afterwards: the champion's "
        + "mastery lastPlayTime was older than the configured inactivity window, or Riot returned no "
        + "mastery entry for that champion at all.";

    private const string EffectiveThresholdNote =
        "The effective IsMain threshold for a given champion sits between the floor and the base "
        + "threshold, interpolated by that champion's coverage deficit. The deficit is computed inside "
        + "the Ingestor at analysis time and never persisted, so only the band can be shown here.";

    public async Task<AccountExplorerReadModel> GetAsync(
        string gameName,
        string tagLine,
        string? platformId,
        CancellationToken ct)
    {
        var normalizedName = gameName.Trim();
        var normalizedTag = tagLine.Trim();
        var query = new AccountExplorerQueryReadModel
        {
            GameName = normalizedName,
            TagLine = normalizedTag,
            Region = platformId
        };

        var accounts = await ResolveAccountsAsync(normalizedName, normalizedTag, platformId, ct);

        if (accounts.Count == 0)
        {
            return await BuildUnknownAsync(query, normalizedName, normalizedTag, platformId, ct);
        }

        // Most recently active row wins, exactly as the public profile and activity
        // reads disambiguate — otherwise this panel and the site would name
        // different accounts for the same Riot ID.
        var account = accounts[0];

        var candidates = await ReadCandidatesAsync(account, ct);
        var mainRows = await ReadMainRowsAsync(account, ct);
        var matchesIngested = await ReadMatchesIngestedAsync(account, mainRows, ct);
        var rankSnapshots = await ReadRankSnapshotsAsync(account.Id, ct);
        var seedRequest = await ReadSeedRequestAsync(account, normalizedName, normalizedTag, ct);

        var hasQueuedCandidate = candidates.Any(c => c.Status == nameof(MainCandidateStatus.Queued));
        var hasActiveMain = mainRows.Any(m => m is { IsMain: true, IsActive: true });
        var hasAnyMainRow = mainRows.Any(m => m.IsMain);

        var state = ResolveState(account, hasQueuedCandidate, hasActiveMain, hasAnyMainRow, mainRows, candidates);

        return new AccountExplorerReadModel
        {
            Query = query,
            State = state.ToString(),
            StateDetail = DescribeState(state, account, candidates, mainRows, matchesIngested),
            Identity = ToIdentity(account),
            OtherAccountsWithSameRiotId = accounts.Skip(1).Select(ToAccountRef).ToList(),
            Tracking = BuildTracking(account, hasActiveMain, hasQueuedCandidate),
            MatchesIngested = matchesIngested,
            Candidates = candidates,
            SeedRequest = seedRequest,
            Mains = new AccountExplorerMainsReadModel
            {
                Rows = mainRows,
                Thresholds = BuildThresholds()
            },
            RankSnapshots = rankSnapshots
        };
    }

    /// <summary>
    /// Every account carrying this Riot ID, most recently active first. Matched
    /// case-insensitively: <c>(GameName, TagLine, PlatformId)</c> is stored as
    /// submitted and an operator retyping a Riot ID should not get
    /// "never discovered" over a capitalisation difference. The patterns are
    /// escaped and wildcard-free, so this is an exact match modulo case.
    /// </summary>
    private async Task<List<RiotAccount>> ResolveAccountsAsync(
        string gameName,
        string tagLine,
        string? platformId,
        CancellationToken ct)
    {
        var namePattern = LikeEscaping.Escape(gameName);
        var tagPattern = LikeEscaping.Escape(tagLine);

        var query = db.RiotAccounts
            .AsNoTracking()
            .Where(a => EF.Functions.ILike(a.GameName, namePattern, LikeEscaping.EscapeChar)
                        && a.TagLine != null
                        && EF.Functions.ILike(a.TagLine, tagPattern, LikeEscaping.EscapeChar));

        if (platformId is not null)
        {
            query = query.Where(a => a.PlatformId == platformId);
        }

        return await query
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .ThenBy(a => a.Id)
            .Take(AccountCollisionCap)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The answer when no account row exists. A candidate cannot be found from
    /// here — <c>main_candidates</c> is keyed on <c>(PlatformId, Puuid)</c> and
    /// carries no Riot ID — but a manual seed request can, because it stores the
    /// Riot ID as typed. That distinction is the difference between "we never saw
    /// this" and "an operator asked for it and the resolution failed".
    /// </summary>
    private async Task<AccountExplorerReadModel> BuildUnknownAsync(
        AccountExplorerQueryReadModel query,
        string gameName,
        string tagLine,
        string? platformId,
        CancellationToken ct)
    {
        var seedRequest = await FindSeedRequestByRiotIdAsync(gameName, tagLine, platformId, ct);

        var state = seedRequest is null
            ? AccountPipelineState.NeverDiscovered
            : AccountPipelineState.SeedRequestedOnly;

        var detail = seedRequest is null
            ? "No riot_accounts row and no seed request carries this Riot ID: the pipeline has never "
              + "encountered it. This read never calls Riot, so it cannot say whether the Riot ID exists."
            : $"No riot_accounts row exists yet, but a manual seed request from "
              + $"{Format(seedRequest.RequestedAtUtc)} is on record with status {seedRequest.Status}"
              + (string.IsNullOrWhiteSpace(seedRequest.Error) ? "." : $" ({seedRequest.Error}).");

        return new AccountExplorerReadModel
        {
            Query = query,
            State = state.ToString(),
            StateDetail = detail,
            SeedRequest = seedRequest,
            Mains = new AccountExplorerMainsReadModel { Thresholds = BuildThresholds() }
        };
    }

    private async Task<IReadOnlyList<AccountExplorerCandidateReadModel>> ReadCandidatesAsync(
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

    private async Task<IReadOnlyList<AccountExplorerMainRowReadModel>> ReadMainRowsAsync(
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
    private async Task<AccountExplorerMatchesIngestedReadModel> ReadMatchesIngestedAsync(
        RiotAccount account,
        IReadOnlyList<AccountExplorerMainRowReadModel> mainRows,
        CancellationToken ct)
    {
        // Puuid alone, deliberately: it is globally unique (unlike the
        // (GameName, TagLine, PlatformId) triple resolved above), and a game
        // played before a region transfer still belongs to this account even
        // though its PlatformId no longer matches the account's current one.
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

        var (pruned, prunedNote) = EvaluatePruning(
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

    /// <summary>
    /// Decides whether retention has demonstrably deleted this account's games,
    /// and says so in words either way. The detection only works through the
    /// frozen aggregates, which cover main champions alone — so a negative is
    /// "no pruning is detectable", never "nothing was pruned".
    /// </summary>
    private static (bool Pruned, string Note) EvaluatePruning(
        long liveCount,
        long careerGames,
        int patchCount,
        DateTime? oldestRetained,
        DateTime? oldestAggregated)
    {
        const string BlindSpot =
            "A negative here is not proof: the frozen aggregates only ever folded main champions, so "
            + "games on other champions can be deleted without leaving anything to detect them by.";

        if (careerGames == 0)
        {
            return liveCount == 0
                ? (false, "Nothing has been ingested for this account and no frozen aggregate exists, so "
                          + "this is an absence of data rather than a deletion. " + BlindSpot)
                : (false, $"{liveCount} participant row(s) are on disk and no frozen aggregate exists to "
                          + "compare them against, so nothing can be said about deletions. " + BlindSpot);
        }

        if (liveCount == 0)
        {
            return (true, $"Retention has deleted this account's games: the frozen aggregates account for "
                          + $"{careerGames} game(s) across {patchCount} patch(es), and no participant row "
                          + "survives. The zero above is a storage window, not a play history.");
        }

        if (oldestAggregated is not null && oldestRetained is not null && oldestAggregated < oldestRetained)
        {
            return (true, $"Retention has deleted part of this account's history: the frozen aggregates "
                          + $"reach back to at least {Format(oldestAggregated.Value)}, while the oldest "
                          + $"surviving participant row is from {Format(oldestRetained.Value)}.");
        }

        return (false, $"No deletion is detectable: the frozen aggregates ({careerGames} game(s) over "
                       + $"{patchCount} patch(es)) do not reach further back than the surviving participant "
                       + "rows. " + BlindSpot);
    }

    private async Task<IReadOnlyList<AccountExplorerRankSnapshotReadModel>> ReadRankSnapshotsAsync(
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

    /// <summary>
    /// The manual-seed trail for a resolved account. Preferred match is the
    /// resolved PUUID; the Riot-ID fallback catches a request that failed before
    /// it ever resolved one, on an account discovered by some other route.
    /// </summary>
    private async Task<SeedRequestReadModel?> ReadSeedRequestAsync(
        RiotAccount account,
        string gameName,
        string tagLine,
        CancellationToken ct)
    {
        var document = await seedRequestStore.GetLatestResolvedForAccountAsync(
            account.Puuid, account.PlatformId, ct);

        return document is not null
            ? SeedRequestQueryService.ToReadModel(document)
            : await FindSeedRequestByRiotIdAsync(gameName, tagLine, account.PlatformId, ct);
    }

    /// <summary>
    /// Newest seed request whose Riot ID matches exactly. The store searches with a
    /// contains-match on name or tag, so the exact pair (and the platform, when
    /// one was requested) is filtered here.
    /// </summary>
    private async Task<SeedRequestReadModel?> FindSeedRequestByRiotIdAsync(
        string gameName,
        string tagLine,
        string? platformId,
        CancellationToken ct)
    {
        var documents = await seedRequestStore.GetRecentAsync(
            status: null, search: gameName, limit: SeedRequestScanLimit, ct);

        var match = documents.FirstOrDefault(d =>
            string.Equals(d.GameName, gameName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.TagLine, tagLine, StringComparison.OrdinalIgnoreCase)
            && (platformId is null || string.Equals(d.PlatformId, platformId, StringComparison.OrdinalIgnoreCase)));

        return match is null ? null : SeedRequestQueryService.ToReadModel(match);
    }

    private static AccountPipelineState ResolveState(
        RiotAccount account,
        bool hasQueuedCandidate,
        bool hasActiveMain,
        bool hasAnyMainRow,
        IReadOnlyList<AccountExplorerMainRowReadModel> mainRows,
        IReadOnlyList<AccountExplorerCandidateReadModel> candidates)
    {
        // Invalid comes first: nothing downstream will ever move again, so any
        // other label would describe a state the account can no longer leave.
        if (account.Status == RiotAccountStatus.Invalid)
        {
            return AccountPipelineState.Invalidated;
        }

        if (hasActiveMain || hasQueuedCandidate)
        {
            return AccountPipelineState.Tracked;
        }

        if (hasAnyMainRow)
        {
            return AccountPipelineState.Retired;
        }

        if (mainRows.Count > 0)
        {
            return AccountPipelineState.NotAMain;
        }

        return candidates.Count > 0
            ? AccountPipelineState.CandidateOnly
            : AccountPipelineState.Discovered;
    }

    private static string DescribeState(
        AccountPipelineState state,
        RiotAccount account,
        IReadOnlyList<AccountExplorerCandidateReadModel> candidates,
        IReadOnlyList<AccountExplorerMainRowReadModel> mainRows,
        AccountExplorerMatchesIngestedReadModel matches)
        => state switch
        {
            AccountPipelineState.Invalidated =>
                "account-v1 no longer resolves this PUUID and AccountRefresh could not recover it by Riot "
                + "ID, so the row is marked Invalid. It is kept for history but excluded from every refresh "
                + "and ingest selection: nothing downstream will move again until the account is re-seeded.",

            AccountPipelineState.Tracked =>
                $"In the match-ingestion population, with {matches.LiveParticipantCount} participant row(s) "
                + $"currently on disk and {mainRows.Count(m => m is { IsMain: true, IsActive: true })} active "
                + "main(s). "
                + (account.LastMatchIngestAtUtc is null
                    ? "Its lease has never come up, so no games have been fetched yet."
                    : $"Last ingested {Format(account.LastMatchIngestAtUtc.Value)}."),

            AccountPipelineState.Retired =>
                $"MainActivity has retired every one of this account's {mainRows.Count(m => m.IsMain)} main "
                + "row(s): they are flagged inactive rather than deleted, so the account drops off the site "
                + "and stops consuming match-v5 calls while its history stays readable. Playing the champion "
                + "again reactivates the row without a fresh discovery.",

            AccountPipelineState.NotAMain =>
                $"MainAnalysis has written {mainRows.Count} champion row(s) for this account but promoted "
                + "none of them past the adaptive IsMain floor, so the account is analysed and simply is not "
                + "a main of anything. The play rates and the threshold band below say by how much.",

            AccountPipelineState.CandidateOnly =>
                $"Known to the candidate funnel ({candidates.Count} row(s), status "
                + $"{DescribeStatuses(candidates)}) but never analysed: main_champion_stats holds nothing "
                + "for it. "
                + (account.LastMainCalcAtUtc is null
                    ? "MainAnalysis has never run on this account."
                    : $"MainAnalysis last ran on it {Format(account.LastMainCalcAtUtc.Value)}."),

            AccountPipelineState.Discovered =>
                "The account exists and nothing else has happened to it: no candidate row, no analysed "
                + "champion, and neither membership arm of the ingest claim matches — so it is never "
                + "selected for match ingestion.",

            _ => string.Empty
        };

    /// <summary>
    /// Every distinct candidate status, in funnel order. Deliberately not a
    /// "furthest status": <c>Rejected</c> is the highest enum value but the worst
    /// outcome, so reducing the set to one label would read backwards.
    /// </summary>
    private static string DescribeStatuses(IReadOnlyList<AccountExplorerCandidateReadModel> candidates)
        => string.Join(", ", candidates
            .Select(c => Enum.TryParse<MainCandidateStatus>(c.Status, out var parsed)
                ? parsed
                : MainCandidateStatus.New)
            .Distinct()
            .OrderBy(status => status)
            .Select(status => status.ToString()));

    private AccountExplorerTrackingReadModel BuildTracking(
        RiotAccount account,
        bool hasActiveMain,
        bool hasQueuedCandidate)
    {
        // The real ingest claim (ClaimAccountsForMatchIngestAtomicallyAsync) gates
        // on RiotAccountStatus.Active before either membership arm is even
        // evaluated. An Invalidated account can still carry a stale IsMain row
        // from before it was invalidated, so membership alone would report it as
        // tracked while the state banner above says Invalidated — the exact
        // contradiction this page exists to prevent. Eligibility must agree with
        // the real gate.
        var eligible = account.Status == RiotAccountStatus.Active;

        var trackedVia = eligible
            ? (hasActiveMain, hasQueuedCandidate) switch
            {
                (true, true) => "Both",
                (true, false) => "EstablishedMain",
                (false, true) => "QueuedCandidate",
                _ => null
            }
            : null;

        var claimAge = account.MatchIngestClaimedAtUtc is null
            ? (double?)null
            : (timeProvider.GetUtcNow().UtcDateTime - account.MatchIngestClaimedAtUtc.Value).TotalSeconds;

        return new AccountExplorerTrackingReadModel
        {
            IsTracked = trackedVia is not null,
            TrackedVia = trackedVia,
            HasActiveMain = hasActiveMain,
            HasQueuedCandidate = hasQueuedCandidate,
            MatchIngestStatus = account.MatchIngestStatus.ToString(),
            MatchIngestClaimedAtUtc = account.MatchIngestClaimedAtUtc,
            ClaimAgeSeconds = claimAge,
            LastMatchIngestAtUtc = account.LastMatchIngestAtUtc,
            NeverIngested = account.LastMatchIngestAtUtc is null
        };
    }

    private AccountExplorerMainThresholdsReadModel BuildThresholds()
    {
        var options = mainAnalysisOptions.Value;

        return new AccountExplorerMainThresholdsReadModel
        {
            PlayRateThreshold = options.PlayRateThreshold,
            PlayRateFloor = options.PlayRateFloor,
            OtpPlayRateThreshold = options.OtpPlayRateThreshold,
            MinMatchesToEvaluate = options.MinMatchesToEvaluate,
            EffectiveThresholdNote = EffectiveThresholdNote
        };
    }

    private static AccountExplorerIdentityReadModel ToIdentity(RiotAccount account)
        => new()
        {
            RiotAccountId = account.Id,
            Puuid = account.Puuid,
            GameName = account.GameName,
            TagLine = account.TagLine,
            PlatformId = account.PlatformId,
            ProfileIconId = account.ProfileIconId,
            SummonerLevel = account.SummonerLevel,
            Status = account.Status.ToString(),
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = account.UpdatedAtUtc,
            LastProfileSyncAtUtc = account.LastProfileSyncAtUtc,
            LastRankSyncAtUtc = account.LastRankSyncAtUtc,
            LastMainCalcAtUtc = account.LastMainCalcAtUtc,
            LastActivityCheckAtUtc = account.LastActivityCheckAtUtc,
            LastMatchIngestAtUtc = account.LastMatchIngestAtUtc,
            RankScore = account.Score
        };

    private static AccountExplorerAccountRefReadModel ToAccountRef(RiotAccount account)
        => new()
        {
            RiotAccountId = account.Id,
            Puuid = account.Puuid,
            PlatformId = account.PlatformId,
            Status = account.Status.ToString(),
            LastMatchIngestAtUtc = account.LastMatchIngestAtUtc
        };

    private static string Format(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
}
