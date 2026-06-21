using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public sealed class SearchQueryService(
    TrueMainDbContext db,
    ILogger<SearchQueryService> logger) : ISearchQueryService
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 25;

    // The trigram index only earns its keep from two characters up; a single
    // letter would match a huge slice of the table and the planner would fall
    // back to a scan anyway. Below this we return nothing rather than paying
    // for that — the frontend keeps showing its "keep typing" hint.
    // Keep in sync with SEARCH_MIN_LENGTH in
    // web/app/composables/useTruemainSearch.ts (no shared contract enforces it).
    private const int MinQueryLength = 2;

    // Upper bound on the query length. A real Riot id slug ("Name#TAG") is well
    // under this — the DB caps GameName at 32 and TagLine at 8 — so anything
    // longer is junk or abuse; reject it before it reaches EscapeLike / ILIKE.
    private const int MaxQueryLength = 64;

    // The exposed-region platform list is static, so materialise it once rather
    // than rebuilding the array on every search.
    private static readonly string[] ExposedPlatforms = RegionFilterParser.AllExposedPlatforms().ToArray();

    public async Task<SearchResponse> SearchAsync(string? query, int limit, CancellationToken ct)
    {
        var parsed = ParseQuery(query);
        if (parsed is null)
        {
            // Symmetric with the result-count logs below: a query rejected as
            // unsearchable (too short, too long, or empty) still leaves a trace,
            // so a "search not responding" report can tell a server-side
            // rejection from a request that never left the client. Length only —
            // not the raw term — to keep the line cheap and noise-free.
            logger.LogDebug("[truemain-search] query rejected as unsearchable (length={Length})", query?.Length ?? 0);
            return Empty;
        }

        var (name, tag) = parsed.Value;
        var clampedLimit = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

        // Mirror the leaderboard population: only ranked main accounts on the
        // exposed regions are "truemains", so search is a faster path into the
        // same list rather than a window onto every discovered account.
        var platforms = ExposedPlatforms;

        // Case-insensitive substring match on GameName, served by the
        // gin_trgm_ops index on riot_accounts."GameName" (see the
        // AddRiotAccountGameNameTrgmIndex migration). The wildcards in the
        // user's input are escaped so a literal '%' or '_' can't widen the
        // match. The escape character is passed explicitly to ILike below:
        // EF's 2-arg form emits ESCAPE '' (escaping disabled), which would make
        // our '\' a literal and break the match, so we use the 3-arg overload.
        var pattern = $"%{EscapeLike(name)}%";
        var nameLower = name.ToLowerInvariant();

        var accounts = db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.Score != null
                        && platforms.Contains(a.PlatformId)
                        && EF.Functions.ILike(a.GameName, pattern, "\\")
                        && db.MainChampionStats.Any(m =>
                            m.PlatformId == a.PlatformId && m.Puuid == a.Puuid && m.IsMain));

        if (tag is not null)
        {
            // Exact, case-insensitive tag match via lower() equality —
            // deliberately NOT ILike. The tag is raw user input (everything
            // after '#'), and EF's 2-arg ILike emits ESCAPE '' (escaping off),
            // so a `Name#%` query would turn the tag into a bare wildcard and
            // match every tag line. A lower() equality sidesteps LIKE
            // metacharacters entirely. Riot tag lines are short fixed strings
            // (e.g. "EUW", "NA1"), so exact match is the right contract anyway;
            // the name half stays a substring search.
            var tagLower = tag.ToLowerInvariant();
            accounts = accounts.Where(a => a.TagLine != null && a.TagLine.ToLower() == tagLower);
        }

        var rows = await accounts
            // Exact (case-insensitive) name first, then ranked accounts by
            // descending sort key, then alphabetically as a stable tiebreak.
            // The name tiebreak is lower()'d so it's case-insensitive (the
            // column's default collation would otherwise sort 'A' before 'a').
            .OrderByDescending(a => a.GameName.ToLower() == nameLower)
            .ThenByDescending(a => a.Score)
            .ThenBy(a => a.GameName.ToLower())
            .Take(clampedLimit)
            .Select(a => new AccountRow(
                a.Id,
                a.GameName,
                a.TagLine,
                a.PlatformId,
                a.ProfileIconId,
                a.SummonerLevel))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            logger.LogInformation(
                "[truemain-search] query={Query} tag={Tag} results=0",
                name, tag ?? "any");
            return Empty;
        }

        var ranksByAccount = await FetchLatestRanksAsync(rows.Select(r => r.Id).ToArray(), ct);

        var results = rows
            .Select(r =>
            {
                var rank = ranksByAccount.GetValueOrDefault(r.Id);
                return new SearchResultReadModel
                {
                    Identity = new ProfileIdentityReadModel
                    {
                        GameName = r.GameName,
                        TagLine = r.TagLine,
                        PlatformId = r.PlatformId,
                        ProfileIconId = r.ProfileIconId,
                        SummonerLevel = r.SummonerLevel,
                    },
                    Region = RegionFilterParser.RouteToSlug(r.PlatformId) ?? string.Empty,
                    Ranked = rank is null
                        ? null
                        : new SearchRankedReadModel
                        {
                            Tier = rank.Tier,
                            Division = rank.Division,
                            LeaguePoints = rank.LeaguePoints,
                        },
                };
            })
            .ToList();

        logger.LogInformation(
            "[truemain-search] query={Query} tag={Tag} results={Results}",
            name, tag ?? "any", results.Count);

        return new SearchResponse { Results = results };
    }

    /// <summary>
    /// Splits the raw query into a name prefix and an optional tag. A '#'
    /// separates the Riot id (the way users type it: <c>Name#TAG</c>); '-' is
    /// deliberately not a separator here because game names contain hyphens, so
    /// splitting on it would mangle the search term. Returns null when there's
    /// nothing searchable (empty, or a name below the minimum length).
    /// </summary>
    private static (string Name, string? Tag)? ParseQuery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxQueryLength)
        {
            return null;
        }

        var hashIdx = trimmed.IndexOf('#');

        string name;
        string? tag;
        if (hashIdx >= 0)
        {
            name = trimmed[..hashIdx].Trim();
            var rawTag = trimmed[(hashIdx + 1)..].Trim();
            tag = rawTag.Length == 0 ? null : rawTag;
        }
        else
        {
            name = trimmed;
            tag = null;
        }

        return name.Length < MinQueryLength ? null : (name, tag);
    }

    // Escape the LIKE/ILIKE metacharacters so user input is matched literally;
    // '\' first so the escapes we add aren't themselves escaped.
    private static string EscapeLike(string input) => input
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private async Task<Dictionary<Guid, RankRow>> FetchLatestRanksAsync(Guid[] accountIds, CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return new Dictionary<Guid, RankRow>();
        }

        // Latest snapshot per account for the display cell — the same DISTINCT
        // ON shape the leaderboard uses, scoped to the handful of result rows.
        FormattableString sql = $"""
            SELECT DISTINCT ON (rs."RiotAccountId")
                rs."RiotAccountId" AS "AccountId",
                rs."Tier" AS "Tier",
                rs."Division" AS "Division",
                rs."LeaguePoints" AS "LeaguePoints"
            FROM rank_snapshots rs
            WHERE rs."RiotAccountId" = ANY ({accountIds})
            ORDER BY rs."RiotAccountId", rs."CapturedAtUtc" DESC
            """;

        var rows = await db.Database.SqlQuery<RankRow>(sql).ToListAsync(ct);
        return rows.ToDictionary(r => r.AccountId);
    }

    private static readonly SearchResponse Empty = new();

    private sealed record AccountRow(
        Guid Id,
        string GameName,
        string? TagLine,
        string PlatformId,
        int ProfileIconId,
        int SummonerLevel);

    private sealed record RankRow(Guid AccountId, string Tier, string Division, int LeaguePoints);
}
