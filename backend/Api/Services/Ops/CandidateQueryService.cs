using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the admin Candidates panel. The list left-joins
/// <c>MainCandidate</c> to <c>RiotAccount</c> on PUUID so each row carries the
/// account's Riot ID when it has been resolved (a candidate is discovered from
/// mastery before its account is upserted, so the join is intentionally optional).
/// Search matches that joined Riot ID, the PUUID, or — when the term parses as an
/// integer — the champion id; rows are ordered most-relevant first (highest score,
/// then most recently discovered). Detail additionally counts the account's
/// ingested <c>MatchParticipant</c> rows and surfaces the manual <c>SeedRequest</c>
/// that brought the account in, matched on <c>ResolvedPuuid</c> + platform.
/// </summary>
public sealed class CandidateQueryService(TrueMainDbContext db) : ICandidateQueryService
{
    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

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

        // Left-join each candidate to its RiotAccount on PUUID. A candidate is
        // surfaced from mastery before the account is upserted, so the account
        // (and thus the Riot ID) can be absent — the join must not drop the row.
        var query =
            from candidate in db.MainCandidates.AsNoTracking()
            join account in db.RiotAccounts.AsNoTracking()
                on candidate.Puuid equals account.Puuid into accounts
            from account in accounts.DefaultIfEmpty()
            select new { candidate, account };

        if (TryParseStatus(status, out var parsedStatus))
        {
            query = query.Where(row => row.candidate.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            var platform = platformId.Trim();
            query = query.Where(row => row.candidate.PlatformId.ToUpper() == platform.ToUpper());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{LikeEscaping.Escape(term)}%";

            // championId is matched only when the whole term is an integer, so a
            // Riot ID like "abc" never accidentally filters on a champion.
            int? championId = int.TryParse(term, out var parsedChampionId) ? parsedChampionId : null;

            query = query.Where(row =>
                (row.account != null && EF.Functions.ILike(row.account.GameName, pattern, LikeEscaping.EscapeChar))
                || (row.account != null && row.account.TagLine != null
                    && EF.Functions.ILike(row.account.TagLine, pattern, LikeEscaping.EscapeChar))
                || EF.Functions.ILike(row.candidate.Puuid, pattern, LikeEscaping.EscapeChar)
                || (championId != null && row.candidate.ChampionId == championId));
        }

        var total = await query.LongCountAsync(ct);

        var rows = await query
            // Most-relevant first: highest score, then most recently discovered.
            // Id breaks ties so paging is stable when rows share a score + time.
            .OrderByDescending(row => row.candidate.Score)
            .ThenByDescending(row => row.candidate.DiscoveredAtUtc)
            .ThenByDescending(row => row.candidate.Id)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(row => new CandidateRowReadModel
            {
                Id = row.candidate.Id,
                PlatformId = row.candidate.PlatformId,
                Puuid = row.candidate.Puuid,
                GameName = row.account != null ? row.account.GameName : null,
                TagLine = row.account != null ? row.account.TagLine : null,
                ChampionId = row.candidate.ChampionId,
                ChampionPoints = row.candidate.ChampionPoints,
                ChampionRankInMasteryTop = row.candidate.ChampionRankInMasteryTop,
                Score = row.candidate.Score,
                Status = row.candidate.Status.ToString(),
                DiscoveredAtUtc = row.candidate.DiscoveredAtUtc,
                ScoredAtUtc = row.candidate.ScoredAtUtc,
                ValidatedAtUtc = row.candidate.ValidatedAtUtc,
                LastPlayTimeUtc = row.candidate.LastPlayTimeUtc
            })
            .ToListAsync(ct);

        return new CandidatesReadModel
        {
            Candidates = rows,
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
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

        // The manual seed request that resolved to this account, if any. Matched on
        // ResolvedPuuid + PlatformId so an organically-discovered candidate (never
        // seeded) gets none; newest-first in case the same Riot ID was seeded more
        // than once across resets.
        var seedRequest = await db.SeedRequests
            .AsNoTracking()
            .Where(request => request.ResolvedPuuid == candidate.Puuid
                && request.PlatformId == candidate.PlatformId)
            .OrderByDescending(request => request.RequestedAtUtc)
            .ThenByDescending(request => request.Id)
            .Select(request => new SeedRequestReadModel
            {
                Id = request.Id,
                GameName = request.GameName,
                TagLine = request.TagLine,
                PlatformId = request.PlatformId,
                Status = request.Status.ToString(),
                Error = request.Error,
                RequestedAtUtc = request.RequestedAtUtc,
                ProcessedAtUtc = request.ProcessedAtUtc,
                ResolvedPuuid = request.ResolvedPuuid,
                ResolvedRiotAccountId = request.ResolvedRiotAccountId
            })
            .FirstOrDefaultAsync(ct);

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
