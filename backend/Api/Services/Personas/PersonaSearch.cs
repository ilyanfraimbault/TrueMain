using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Personas;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Leaderboard;

namespace TrueMain.Services.Personas;

/// <summary>
/// The known players a search term names (#2018): a case-insensitive substring of the display name
/// or the slug, so "irel" finds Irelking whichever of his Riot IDs the visitor never knew. Each
/// match carries the face and rank of the person's best tracked account, the one the search row
/// draws. <c>personas</c> is a few thousand rows, so the ILIKE needs no index of its own.
/// </summary>
internal static class PersonaSearch
{
    public const int Limit = 5;

    private const int CandidateCap = 50;

    /// <summary>Up to <see cref="Limit"/> known players matching the term, exact names first, then best rank.</summary>
    /// <param name="db">The caller's context.</param>
    /// <param name="pattern">An escaped ILIKE pattern (<c>%term%</c>, '\' as the escape character).</param>
    /// <param name="termLower">The lowered term, ranking an exact name or slug first.</param>
    /// <param name="ct">Request cancellation token.</param>
    public static async Task<IReadOnlyList<SearchPlayerReadModel>> FetchAsync(
        TrueMainDbContext db,
        string pattern,
        string termLower,
        CancellationToken ct)
    {
        // Matches are bounded before ranking: a two-letter term can name hundreds of people, and the
        // rank that orders them lives on their accounts, read in a second keyed query.
        var matches = await db.Personas
            .AsNoTracking()
            .Where(p => EF.Functions.ILike(p.DisplayName, pattern, "\\") || EF.Functions.ILike(p.Slug, pattern, "\\"))
            // Their page lists tracked accounts only, so a person with none has no page to open.
            .Where(p => p.Accounts.Any(a => a.RiotAccountId != null))
            .OrderBy(p => p.Slug)
            .Take(CandidateCap)
            .Select(p => new { p.Id, p.Slug, p.DisplayName, Accounts = p.Accounts.Count(a => a.RiotAccountId != null) })
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            return [];
        }

        var matchIds = matches.Select(p => p.Id).ToArray();
        var tracked = await db.PersonaAccounts
            .AsNoTracking()
            .Where(a => matchIds.Contains(a.PersonaId) && a.RiotAccountId != null)
            .Join(
                db.RiotAccounts.AsNoTracking(),
                link => link.RiotAccountId,
                account => account.Id,
                (link, account) => new { link.PersonaId, Best = new BestAccount(account.Id, account.ProfileIconId, account.Score) })
            .ToListAsync(ct);
        var bestByPersona = tracked
            .GroupBy(row => row.PersonaId)
            .ToDictionary(group => group.Key, group => group.MaxBy(row => row.Best.Score ?? int.MinValue)!.Best);

        var personas = matches
            .Select(p => new
            {
                p.Slug,
                p.DisplayName,
                p.Accounts,
                Exact = string.Equals(p.DisplayName, termLower, StringComparison.OrdinalIgnoreCase) || p.Slug == termLower,
                Best = bestByPersona.GetValueOrDefault(p.Id),
            })
            .OrderByDescending(p => p.Exact)
            .ThenByDescending(p => p.Best?.Score ?? int.MinValue)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Limit)
            .ToList();

        var bestIds = personas.Where(p => p.Best != null).Select(p => p.Best!.Id).ToArray();
        var ranks = await LatestRankSnapshots.FetchAsync(db, bestIds, ct);

        return personas
            .Select(p =>
            {
                var rank = p.Best is null ? null : ranks.GetValueOrDefault(p.Best.Id);
                return new SearchPlayerReadModel
                {
                    Persona = new PersonaTagReadModel { Slug = p.Slug, DisplayName = p.DisplayName },
                    AccountCount = p.Accounts,
                    ProfileIconId = p.Best?.ProfileIconId,
                    Ranked = rank is null || string.IsNullOrEmpty(rank.Tier)
                        ? null
                        : new SearchRankedReadModel { Tier = rank.Tier, Division = rank.Division, LeaguePoints = rank.LeaguePoints },
                };
            })
            .ToList();
    }

    private sealed record BestAccount(Guid Id, int ProfileIconId, int? Score);
}
