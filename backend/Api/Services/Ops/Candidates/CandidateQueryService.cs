using Core.Truemains;
using Data;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Accounts;
using TrueMain.Services.Sql;

namespace TrueMain.Services.Ops.Candidates;

/// <summary>
/// Read path for the admin Candidates panel: the main-candidate ingestion
/// pipeline list (filterable/searchable/paged) and a single candidate's detail
/// (its pipeline fields plus the joined account, ingested match count, and the
/// linked manual seed request when one exists). Projects read-models with
/// <c>AsNoTracking</c>; no generic repository.
/// </summary>
public interface ICandidateQueryService
{
    /// <summary>
    /// A page of main candidates, newest-first, optionally filtered and searched.
    /// </summary>
    /// <param name="status">
    /// Restrict to a single <c>MainCandidateStatus</c> (case-insensitive name);
    /// null/blank/unknown means all statuses.
    /// </param>
    /// <param name="platformId">
    /// Restrict to one platform/region (e.g. "EUW1", case-insensitive); null/blank
    /// means all regions.
    /// </param>
    /// <param name="search">
    /// Free-text search: matches the joined Riot ID (gameName/tagLine), the PUUID,
    /// or — when the term is numeric — the champion id. Null/blank means no search.
    /// </param>
    /// <param name="page">1-based page index (clamped to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (clamped to a safe range).</param>
    /// <param name="ct">Request cancellation token.</param>
    Task<CandidatesReadModel> GetCandidatesAsync(
        string? status,
        string? platformId,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct);

    /// <summary>
    /// Detail for one candidate by id: its pipeline fields, the joined account
    /// identity, the ingested match count for its PUUID, and the linked
    /// <c>SeedRequest</c> when one matches its <c>ResolvedPuuid</c> + platform.
    /// Null when no candidate has the given id.
    /// </summary>
    Task<CandidateDetailReadModel?> GetByIdAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Read path for the admin Candidates panel. The list left-joins
/// <c>MainCandidate</c> to <c>RiotAccount</c> on PUUID so each row carries the
/// account's Riot ID when it has been resolved (a candidate is discovered from
/// mastery before its account is upserted, so the join is intentionally optional).
/// Search matches that joined Riot ID, the PUUID, or — when the term parses as an
/// integer — the champion id; a term written as <c>Name#TAG</c> is split and
/// matched against the two Riot-ID columns instead. Rows are ordered
/// most-relevant first (highest score, then most recently discovered). Detail
/// additionally counts the account's ingested <c>MatchParticipant</c> rows and
/// surfaces the manual <c>SeedRequest</c> that brought the account in, matched
/// on <c>ResolvedPuuid</c> + platform.
/// </summary>
public sealed class CandidateQueryService(
    TrueMainDbContext db,
    ISeedRequestStore seedRequestStore) : ICandidateQueryService
{
    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    /// <summary>
    /// The most rows the pager total counts; a filter matching more answers
    /// <see cref="CandidatesReadModel.TotalCapped"/> with this as the total.
    /// </summary>
    public const int TotalCap = 10_000;

    public async Task<CandidatesReadModel> GetCandidatesAsync(
        string? status,
        string? platformId,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        // Upper bound keeps `(page - 1) * pageSize` within int range even at the
        // maximum page size, mirroring ProcessRunsQueryService.
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        // Filters run on main_candidates alone, so the default list (no search)
        // never touches riot_accounts until the 25 rows of the page are known: the
        // ordering walks IX_main_candidates_list_order (or its Status-led twin)
        // and stops after the page instead of hash-joining the whole table and
        // sorting it (#2304). The Riot-ID search is an uncorrelated
        // `Puuid IN (matching accounts)`, which Postgres runs as one hashed pass
        // over riot_accounts; riot_accounts.Puuid is unique, so it keeps exactly
        // the rows the former left join + filter kept.
        var candidates = db.MainCandidates.AsNoTracking();

        if (TryParseStatus(status, out var parsedStatus))
        {
            candidates = candidates.Where(candidate => candidate.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            var platform = platformId.Trim().ToUpperInvariant();
            candidates = candidates.Where(candidate => candidate.PlatformId.ToUpper() == platform);
        }

        candidates = ApplySearch(candidates, search);

        // Capped count (#2304): counting every row of a wide filter read the whole
        // table on each page turn. A narrow filter still gets its exact total; a
        // wide one stops at the cap and says so.
        var counted = await candidates.Take(TotalCap + 1).LongCountAsync(ct);
        var totalCapped = counted > TotalCap;

        // Most-relevant first: highest score, then most recently discovered. Id
        // breaks ties so paging is stable when rows share a score + time. Offset
        // rather than keyset on purpose (#1635): the operator pages by number.
        var pageOfCandidates = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.DiscoveredAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize);

        // Left-join the page to its RiotAccount on PUUID. A candidate is surfaced
        // from mastery before the account is upserted, so the account (and thus
        // the Riot ID) can be absent — the join must not drop the row.
        var rows = await (
                from candidate in pageOfCandidates
                join account in db.RiotAccounts.AsNoTracking()
                    on candidate.Puuid equals account.Puuid into accounts
                from account in accounts.DefaultIfEmpty()
                orderby candidate.Score descending, candidate.DiscoveredAtUtc descending, candidate.Id descending
                select new CandidateRowReadModel
                {
                    Id = candidate.Id,
                    PlatformId = candidate.PlatformId,
                    Puuid = candidate.Puuid,
                    GameName = account != null ? account.GameName : null,
                    TagLine = account != null ? account.TagLine : null,
                    ChampionId = candidate.ChampionId,
                    ChampionPoints = candidate.ChampionPoints,
                    ChampionRankInMasteryTop = candidate.ChampionRankInMasteryTop,
                    Score = candidate.Score,
                    Status = candidate.Status.ToString(),
                    DiscoveredAtUtc = candidate.DiscoveredAtUtc,
                    ScoredAtUtc = candidate.ScoredAtUtc,
                    ValidatedAtUtc = candidate.ValidatedAtUtc,
                    LastPlayTimeUtc = candidate.LastPlayTimeUtc
                })
            .ToListAsync(ct);

        return new CandidatesReadModel
        {
            Candidates = rows,
            Total = totalCapped ? TotalCap : counted,
            TotalCapped = totalCapped,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    /// <summary>
    /// The free-text search: a term with a '#' is a Riot ID whose halves are
    /// matched against the two columns they name (#1340), never against the
    /// PUUID or the champion id, neither of which can contain one. Any other term
    /// matches the Riot ID, the PUUID, or — when the whole term is an integer —
    /// the champion id.
    /// </summary>
    private IQueryable<MainCandidate> ApplySearch(IQueryable<MainCandidate> candidates, string? search)
    {
        var (namePart, tagPart) = RiotIdSearchTerm.Split(search);

        if (tagPart is not null)
        {
            var tagPattern = $"%{LikeEscaping.Escape(tagPart)}%";
            var accounts = db.RiotAccounts.AsNoTracking().Where(account =>
                account.TagLine != null
                && EF.Functions.ILike(account.TagLine, tagPattern, LikeEscaping.EscapeChar));

            if (namePart is not null)
            {
                var namePattern = $"%{LikeEscaping.Escape(namePart)}%";
                accounts = accounts.Where(account =>
                    EF.Functions.ILike(account.GameName, namePattern, LikeEscaping.EscapeChar));
            }

            var matchingPuuids = accounts.Select(account => account.Puuid);
            return candidates.Where(candidate => matchingPuuids.Contains(candidate.Puuid));
        }

        if (namePart is null)
        {
            return candidates;
        }

        var pattern = $"%{LikeEscaping.Escape(namePart)}%";
        int? championId = int.TryParse(namePart, out var parsedChampionId) ? parsedChampionId : null;
        var puuidsByRiotId = db.RiotAccounts.AsNoTracking()
            .Where(account =>
                EF.Functions.ILike(account.GameName, pattern, LikeEscaping.EscapeChar)
                || (account.TagLine != null
                    && EF.Functions.ILike(account.TagLine, pattern, LikeEscaping.EscapeChar)))
            .Select(account => account.Puuid);

        return candidates.Where(candidate =>
            puuidsByRiotId.Contains(candidate.Puuid)
            || EF.Functions.ILike(candidate.Puuid, pattern, LikeEscaping.EscapeChar)
            || (championId != null && candidate.ChampionId == championId));
    }

    public async Task<CandidateDetailReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var row = await (
                from mainCandidate in db.MainCandidates.AsNoTracking()
                join account in db.RiotAccounts.AsNoTracking()
                    on mainCandidate.Puuid equals account.Puuid into accounts
                from account in accounts.DefaultIfEmpty()
                where mainCandidate.Id == id
                select new
                {
                    Candidate = mainCandidate,
                    GameName = account != null ? account.GameName : null,
                    TagLine = account != null ? account.TagLine : null
                })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var candidate = row.Candidate;

        // How many of the account's games are already ingested (participant rows
        // keyed by PUUID). Counted in the database rather than materialised.
        var ingestedMatchCount = await db.MatchParticipants
            .AsNoTracking()
            .LongCountAsync(participant => participant.Puuid == candidate.Puuid, ct);

        // The manual seed request that resolved to this account, if any (from the
        // Mongo admin store). Matched on ResolvedPuuid + PlatformId so an
        // organically-discovered candidate (never seeded) gets none; newest-first
        // in case the same Riot ID was seeded more than once across resets.
        var seedDocument = await seedRequestStore.GetLatestResolvedForAccountAsync(
            candidate.Puuid, candidate.PlatformId, ct);
        var seedRequest = seedDocument is null
            ? null
            : SeedRequestQueryService.ToReadModel(seedDocument);

        return new CandidateDetailReadModel
        {
            Id = candidate.Id,
            PlatformId = candidate.PlatformId,
            Puuid = candidate.Puuid,
            GameName = row.GameName,
            TagLine = row.TagLine,
            ChampionId = candidate.ChampionId,
            ChampionPoints = candidate.ChampionPoints,
            ChampionRankInMasteryTop = candidate.ChampionRankInMasteryTop,
            Score = candidate.Score,
            Status = candidate.Status.ToString(),
            DiscoveredAtUtc = candidate.DiscoveredAtUtc,
            ScoredAtUtc = candidate.ScoredAtUtc,
            ValidatedAtUtc = candidate.ValidatedAtUtc,
            LastPlayTimeUtc = candidate.LastPlayTimeUtc,
            IngestedMatchCount = ingestedMatchCount,
            SeedRequest = seedRequest
        };
    }

    private static bool TryParseStatus(string? status, out MainCandidateStatus parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(status)
            && Enum.TryParse(status.Trim(), ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }
}
