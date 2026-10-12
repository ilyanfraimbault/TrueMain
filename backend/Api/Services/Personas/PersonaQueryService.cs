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
}

/// <summary>
/// Read side of <c>GET /players/{slug}</c> (#2016): the person, their links, and each listed
/// account with the same rank and top-mains cells the leaderboard computes — through the
/// leaderboard's own fetches, so the two pages cannot disagree about an account.
/// </summary>
public sealed class PersonaQueryService(TrueMainDbContext db) : IPersonaQueryService
{
    public async Task<PersonaReadModel?> GetAsync(string slug, CancellationToken ct)
    {
        var key = slug.Trim().ToLowerInvariant();
        var persona = await db.Personas
            .AsNoTracking()
            .Where(p => p.Slug == key)
            .Select(p => new { p.Id, p.Slug, p.DisplayName })
            .FirstOrDefaultAsync(ct);
        if (persona is null)
        {
            return null;
        }

        var links = await db.PersonaLinks
            .AsNoTracking()
            .Where(link => link.PersonaId == persona.Id)
            .OrderBy(link => link.Kind)
            .ThenBy(link => link.Url)
            .Select(link => new { link.Kind, link.Url })
            .ToListAsync(ct);

        var accounts = await db.PersonaAccounts
            .AsNoTracking()
            .Where(link => link.PersonaId == persona.Id)
            .Select(link => new
            {
                link.GameName,
                link.TagLine,
                link.PlatformId,
                Tracked = link.RiotAccount == null
                    ? null
                    : new
                    {
                        link.RiotAccount.Id,
                        link.RiotAccount.Puuid,
                        link.RiotAccount.GameName,
                        link.RiotAccount.TagLine,
                        link.RiotAccount.PlatformId,
                        link.RiotAccount.ProfileIconId,
                        link.RiotAccount.Score,
                    },
            })
            .ToListAsync(ct);

        var tracked = accounts.Where(a => a.Tracked is not null).Select(a => a.Tracked!).ToList();
        var ranks = await LatestRankSnapshots.FetchAsync(db, tracked.Select(a => a.Id).ToArray(), ct);
        var topChampions = await LeaderboardPageHydration.FetchTopChampionsAsync(
            db,
            tracked.Select(a => a.PlatformId).Distinct().ToArray(),
            tracked.Select(a => a.Puuid).ToArray(),
            ct);

        var rows = accounts
            .OrderBy(a => a.Tracked is null)
            .ThenByDescending(a => a.Tracked?.Score ?? int.MinValue)
            .ThenBy(a => a.GameName, StringComparer.OrdinalIgnoreCase)
            .Select(a =>
            {
                if (a.Tracked is not { } account)
                {
                    return new PersonaAccountReadModel
                    {
                        GameName = a.GameName,
                        TagLine = a.TagLine,
                        PlatformId = a.PlatformId,
                        Region = RegionFilterParser.RouteToSlug(a.PlatformId),
                    };
                }

                var rank = ranks.GetValueOrDefault(account.Id);
                return new PersonaAccountReadModel
                {
                    GameName = account.GameName,
                    TagLine = account.TagLine ?? a.TagLine,
                    PlatformId = account.PlatformId,
                    Region = RegionFilterParser.RouteToSlug(account.PlatformId),
                    Tracked = true,
                    ProfileIconId = account.ProfileIconId,
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
}
