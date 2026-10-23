using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Personas;
using TrueMain.Services.Truemains.Identity;
using TrueMain.Services.Truemains.Leaderboard;

namespace TrueMain.Services.Personas;

public interface IPersonaQueryService
{
    /// <summary>The persona behind <paramref name="slug"/>, or null when none — the controller's 404.</summary>
    Task<PersonaReadModel?> GetAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Every known player with at least one tracked account — the pages worth advertising to a
    /// crawler (#2018). A person whose accounts are all untracked has a page of bare Riot IDs.
    /// </summary>
    Task<PersonaDirectoryResponse> ListAsync(CancellationToken ct);
}

/// <summary>
/// Read side of <c>GET /players/{slug}</c> (#2016): the person, their links, and each tracked
/// account with the same rank and top-mains cells the leaderboard computes — through the
/// leaderboard's own fetches, so the two pages cannot disagree about an account — and the day
/// it last played. An account a source lists but TrueMain does not track has no profile to open,
/// so the page leaves it out; the ops intake keeps it, to link once it is ingested.
/// </summary>
public sealed class PersonaQueryService(TrueMainDbContext db) : IPersonaQueryService
{
    public async Task<PersonaDirectoryResponse> ListAsync(CancellationToken ct)
    {
        var players = await db.Personas
            .AsNoTracking()
            .Where(p => p.Accounts.Any(a => a.RiotAccountId != null))
            .OrderBy(p => p.Slug)
            .Select(p => new PersonaDirectoryEntryReadModel { Slug = p.Slug, UpdatedAtUtc = p.UpdatedAtUtc })
            .ToListAsync(ct);

        return new PersonaDirectoryResponse { Players = players };
    }

    public async Task<PersonaReadModel?> GetAsync(string slug, CancellationToken ct)
    {
        var key = slug.Trim().ToLowerInvariant();
        var persona = await db.Personas
            .AsNoTracking()
            .Where(p => p.Slug == key)
            .Select(p => new { p.Id, p.Slug, p.DisplayName, p.MergedIntoPersonaId })
            .FirstOrDefaultAsync(ct);
        if (persona?.MergedIntoPersonaId is { } keptId)
        {
            // Merged into another persona by an operator (#2253): answer that one — its own slug tells
            // the page to redirect. A merge always points at a live persona, so one hop is enough.
            persona = await db.Personas
                .AsNoTracking()
                .Where(p => p.Id == keptId)
                .Select(p => new { p.Id, p.Slug, p.DisplayName, p.MergedIntoPersonaId })
                .FirstOrDefaultAsync(ct);
        }

        if (persona is null)
        {
            return null;
        }

        // Newest first within a kind (#2177): the intake only adds links, so a renamed channel keeps its old
        // one, and the page shows the first of each kind. One batch shares a timestamp; there a channel's
        // `@handle` form wins over the legacy `/c/`, `/user/` and `/channel/` ones a source also lists.
        var links = await db.PersonaLinks
            .AsNoTracking()
            .Where(link => link.PersonaId == persona.Id)
            .OrderBy(link => link.Kind)
            .ThenByDescending(link => link.CreatedAtUtc)
            .ThenByDescending(link => link.Url.Contains("/@"))
            .ThenBy(link => link.Url)
            .Select(link => new { link.Kind, link.Url })
            .ToListAsync(ct);

        var accounts = await db.PersonaAccounts
            .AsNoTracking()
            .Where(link => link.PersonaId == persona.Id && link.RiotAccountId != null)
            .Join(
                db.RiotAccounts.AsNoTracking(),
                link => link.RiotAccountId,
                account => account.Id,
                (_, account) => new
                {
                    account.Id,
                    account.Puuid,
                    account.GameName,
                    account.TagLine,
                    account.PlatformId,
                    account.ProfileIconId,
                    account.Score,
                    account.LastSeenInMatchAtUtc,
                })
            .ToListAsync(ct);

        // Every account still untracked: no profile to open, nothing to show — the same 404 as an
        // unknown slug, and the reason the sitemap and the search leave such a person out.
        if (accounts.Count == 0)
        {
            return null;
        }

        var accountIds = accounts.Select(a => a.Id).ToArray();
        var ranks = await LatestRankSnapshots.FetchAsync(db, accountIds, ct);
        var lastGames = await FetchLastGamesAsync(accountIds, ct);
        var topChampions = await LeaderboardPageHydration.FetchTopChampionsAsync(
            db,
            accounts.Select(a => a.PlatformId).Distinct().ToArray(),
            accounts.Select(a => a.Puuid).ToArray(),
            ct);

        var rows = accounts
            // The account in use first, the way a visitor looks for the person: most recent game,
            // then best rank for the never-seen ones.
            .Select(account => (Account: account, LastPlayed: Latest(account.LastSeenInMatchAtUtc, lastGames.GetValueOrDefault(account.Id))))
            .OrderByDescending(entry => entry.LastPlayed ?? DateTime.MinValue)
            .ThenByDescending(entry => entry.Account.Score ?? int.MinValue)
            .Select(entry =>
            {
                var account = entry.Account;
                var rank = ranks.GetValueOrDefault(account.Id);
                return new PersonaAccountReadModel
                {
                    GameName = account.GameName,
                    TagLine = account.TagLine ?? string.Empty,
                    PlatformId = account.PlatformId,
                    Region = RegionFilterParser.RouteToSlug(account.PlatformId),
                    ProfileIconId = account.ProfileIconId,
                    LastPlayedAtUtc = entry.LastPlayed,
                    Ranked = rank is null || string.IsNullOrEmpty(rank.Tier)
                        ? null
                        : new PersonaAccountRankedReadModel
                        {
                            Tier = rank.Tier,
                            Division = rank.Division,
                            LeaguePoints = rank.LeaguePoints,
                            Wins = rank.Wins,
                            Losses = rank.Losses,
                        },
                    TopChampions = (topChampions.GetValueOrDefault(account.Puuid) ?? [])
                        .Select(champion => new PersonaAccountChampionReadModel
                        {
                            ChampionId = champion.ChampionId,
                            Games = champion.Games,
                            PlayRate = champion.PlayRate,
                            IsOtp = champion.IsOtp,
                        })
                        .ToList(),
                };
            })
            .ToList();

        return new PersonaReadModel
        {
            Slug = persona.Slug,
            DisplayName = persona.DisplayName,
            Links = links
                .Select(link => new PersonaLinkReadModel { Kind = link.Kind.ToString().ToLowerInvariant(), Url = link.Url })
                .ToList(),
            Accounts = rows,
        };
    }

    /// <summary>
    /// Each account's most recent stored ranked game: the live participant rows while retention
    /// keeps them, else the frozen per-champion aggregates, which outlive them. Both are keyed by
    /// <c>RiotAccountId</c> and indexed on it.
    /// </summary>
    private async Task<Dictionary<Guid, DateTime>> FetchLastGamesAsync(Guid[] accountIds, CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return [];
        }

        var live = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.RiotAccountId != null && accountIds.Contains(p.RiotAccountId.Value))
            .Join(db.Matches.AsNoTracking(), p => p.MatchId, m => m.Id, (p, m) => new { AccountId = p.RiotAccountId!.Value, m.GameStartTimeUtc })
            .GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Last = group.Max(row => row.GameStartTimeUtc) })
            .ToListAsync(ct);

        var frozen = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => accountIds.Contains(scope.RiotAccountId))
            .GroupBy(scope => scope.RiotAccountId)
            .Select(group => new { AccountId = group.Key, Last = group.Max(scope => scope.LastGameStartTimeUtc) })
            .ToListAsync(ct);

        return live.Concat(frozen)
            .GroupBy(row => row.AccountId)
            .ToDictionary(group => group.Key, group => group.Max(row => row.Last));
    }

    private static DateTime? Latest(DateTime? a, DateTime b) =>
        b == default ? a : a is { } value && value > b ? value : b;
}
