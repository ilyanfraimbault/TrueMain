using Core.Options;
using Data;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Sql;

namespace TrueMain.Services.Ops.Accounts;

/// <summary>
/// Read path for the admin account explorer (#1032): everything the pipeline
/// knows about one Riot ID, in one read-model.
/// </summary>
public interface IAccountExplorerQueryService
{
    /// <summary>
    /// Traces one Riot ID through the pipeline. Never returns null: a Riot ID the
    /// pipeline has never seen is a populated read-model in the
    /// <c>NeverDiscovered</c> state, because "we have never seen this account" is
    /// an answer this page exists to give.
    /// </summary>
    /// <param name="gameName">Riot ID game name, already parsed out of the route segment.</param>
    /// <param name="tagLine">Riot ID tag line, already parsed out of the route segment.</param>
    /// <param name="platformId">
    /// Canonical platform id (e.g. "EUW1") to restrict the search to, or null to
    /// search every region. The controller validates it; anything reaching here is
    /// either canonical or null.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    Task<AccountExplorerReadModel> GetAsync(
        string gameName,
        string tagLine,
        string? platformId,
        CancellationToken ct);
}

/// <summary>
/// Traces one Riot ID through the pipeline for the admin account explorer
/// (#1032). The question it answers — "why does this player not show up on the
/// site?" — has a different answer in a different table depending on where the
/// account stalled, so this service resolves the account once and then reads each
/// stage beside it: identity + refresh state, the ingest lease, the candidate
/// funnel, the main-champion rows, and the rank history.
/// <para>
/// This class owns the resolution (which account a Riot ID names, and the
/// seed-request trail when none does) and the assembly. The per-stage Postgres
/// reads live in <see cref="AccountExplorerPanelReads"/>, and the verdicts drawn
/// from them in the pure <see cref="AccountExplorerVerdict"/>.
/// </para>
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

    private const string EffectiveThresholdNote =
        "The effective IsMain threshold for a given champion sits between the floor and the base "
        + "threshold, interpolated by that champion's coverage deficit. The deficit is computed inside "
        + "the Ingestor at analysis time and never persisted, so only the band can be shown here.";

    private readonly AccountExplorerPanelReads panels = new(db);

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

        var candidates = await panels.ReadCandidatesAsync(account, ct);
        var mainRows = await panels.ReadMainRowsAsync(account, ct);
        var matchesIngested = await panels.ReadMatchesIngestedAsync(account, mainRows, ct);
        var rankSnapshots = await panels.ReadRankSnapshotsAsync(account.Id, ct);
        var seedRequest = await ReadSeedRequestAsync(account, normalizedName, normalizedTag, ct);

        var hasQueuedCandidate = AccountExplorerVerdict.HasQueuedCandidate(candidates);
        var hasActiveMain = AccountExplorerVerdict.HasActiveMain(mainRows);

        var state = AccountExplorerVerdict.ResolveState(account, mainRows, candidates);

        return new AccountExplorerReadModel
        {
            Query = query,
            State = state.ToString(),
            StateDetail = AccountExplorerVerdict.DescribeState(state, account, candidates, mainRows, matchesIngested),
            Identity = ToIdentity(account),
            OtherAccountsWithSameRiotId = accounts.Skip(1).Select(ToAccountRef).ToList(),
            Tracking = BuildTracking(
                account, hasActiveMain, hasQueuedCandidate, AccountExplorerVerdict.IsInNewCandidateArm(account, candidates)),
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
              + $"{AccountExplorerVerdict.Format(seedRequest.RequestedAtUtc)} is on record with status "
              + $"{seedRequest.Status}"
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

    private AccountExplorerTrackingReadModel BuildTracking(
        RiotAccount account,
        bool hasActiveMain,
        bool hasQueuedCandidate,
        bool inNewCandidateArm)
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
            ? (hasActiveMain, inNewCandidateArm) switch
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
}
