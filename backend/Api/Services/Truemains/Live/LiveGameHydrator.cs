using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Champions.Draft;
using TrueMain.Services.Truemains.Identity;
using TrueMain.Services.Truemains.Leaderboard;

namespace TrueMain.Services.Truemains.Live;

/// <summary>
/// Turns spectator-v5's game into the live tab's read-model (#2103): the two teams, each
/// player's Solo/Duo rank, and the lane spectator does not give.
///
/// <para>
/// <b>Ranks: our database first.</b> A participant TrueMain holds is ranked from its latest
/// <c>rank_snapshots</c> row; league-v4 is asked only for the others, and each answer
/// (unranked included) is cached per PUUID for <see cref="RankTtl"/> — a lobby's players
/// are re-asked for at most once per game, not once per viewer.
/// </para>
/// </summary>
public sealed class LiveGameHydrator(
    TrueMainDbContext db,
    IRiotLiveClient riot,
    ILanePriorQueryService lanePriors,
    IMemoryCache cache)
{
    public static readonly TimeSpan RankTtl = TimeSpan.FromMinutes(10);

    private const string SoloQueueType = "RANKED_SOLO_5x5";

    private sealed record KnownAccount(Guid Id, string Puuid);

    /// <summary>A cached league-v4 answer; <see cref="Ranked"/> null means unranked.</summary>
    private sealed record RankEntry(ProfileRankedReadModel? Ranked);

    public async Task<LiveGameDetailReadModel> HydrateAsync(
        RiotActiveGameDto game,
        TruemainAccountRef subject,
        CancellationToken ct)
    {
        var puuids = game.Participants
            .Select(p => p.Puuid)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var known = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => puuids.Contains(a.Puuid))
            .Select(a => new KnownAccount(a.Id, a.Puuid))
            .ToListAsync(ct);
        var knownByPuuid = known
            .GroupBy(a => a.Puuid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var ranks = await RanksAsync(subject.PlatformId, puuids, knownByPuuid, ct);
        var positions = await LiveLanes.InferAsync(game, lanePriors, ct);

        var teams = game.Participants
            .Select((participant, index) => (participant, index))
            .GroupBy(p => p.participant.TeamId)
            .OrderBy(g => g.Key)
            .Select(team => new LiveTeamReadModel
            {
                TeamId = team.Key,
                Bans = game.BannedChampions
                    .Where(b => b.TeamId == team.Key && b.ChampionId > 0)
                    .OrderBy(b => b.PickTurn)
                    .Select(b => b.ChampionId)
                    .ToList(),
                Participants = team
                    .OrderBy(p => LiveLanes.Order(positions.GetValueOrDefault(p.index)))
                    .ThenBy(p => p.index)
                    .Select(p => ToParticipant(p.participant, positions.GetValueOrDefault(p.index), subject, knownByPuuid, ranks))
                    .ToList(),
            })
            .ToList();

        return new LiveGameDetailReadModel
        {
            GameId = game.GameId,
            PlatformId = game.PlatformId ?? subject.PlatformId,
            QueueId = game.GameQueueConfigId,
            MapId = game.MapId,
            GameMode = game.GameMode ?? string.Empty,
            StartedAtUtc = game.GameStartTime > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(game.GameStartTime).UtcDateTime
                : null,
            GameLengthSeconds = Math.Max(0, game.GameLength),
            Teams = teams,
        };
    }

    private static LiveParticipantReadModel ToParticipant(
        RiotLiveParticipantDto participant,
        string? position,
        TruemainAccountRef subject,
        Dictionary<string, KnownAccount> knownByPuuid,
        Dictionary<string, ProfileRankedReadModel> ranks)
    {
        var (gameName, tagLine) = SplitRiotId(participant.RiotId);
        var puuid = participant.Puuid;
        var perks = participant.Perks;

        return new LiveParticipantReadModel
        {
            GameName = gameName,
            TagLine = tagLine,
            ChampionId = participant.ChampionId,
            ProfileIconId = participant.ProfileIconId,
            Spell1Id = (int)participant.Spell1Id,
            Spell2Id = (int)participant.Spell2Id,
            KeystoneId = perks?.PerkIds.Count > 0 ? (int)perks.PerkIds[0] : null,
            PrimaryStyleId = perks is { PerkStyle: > 0 } ? (int)perks.PerkStyle : null,
            SecondaryStyleId = perks is { PerkSubStyle: > 0 } ? (int)perks.PerkSubStyle : null,
            Position = position,
            IsBot = participant.Bot,
            IsSubject = puuid is not null && string.Equals(puuid, subject.Puuid, StringComparison.Ordinal),
            IsTracked = puuid is not null && knownByPuuid.ContainsKey(puuid),
            Ranked = puuid is not null ? ranks.GetValueOrDefault(puuid) : null,
        };
    }

    /// <summary>Solo/Duo rank by PUUID, for every participant that has one.</summary>
    private async Task<Dictionary<string, ProfileRankedReadModel>> RanksAsync(
        string platformId,
        List<string> puuids,
        Dictionary<string, KnownAccount> knownByPuuid,
        CancellationToken ct)
    {
        var ranks = new Dictionary<string, ProfileRankedReadModel>(StringComparer.Ordinal);

        var snapshots = await LatestRankSnapshots.FetchAsync(
            db,
            knownByPuuid.Values.Select(a => a.Id).ToArray(),
            ct);
        foreach (var (puuid, account) in knownByPuuid)
        {
            if (snapshots.TryGetValue(account.Id, out var s))
            {
                ranks[puuid] = Ranked(s.Tier, s.Division, s.LeaguePoints, s.Wins, s.Losses);
            }
        }

        var missing = puuids.Where(p => !ranks.ContainsKey(p)).ToList();
        var fetched = await Task.WhenAll(missing.Select(async puuid =>
            (puuid, entry: await RankFromRiotAsync(platformId, puuid, ct))));
        foreach (var (puuid, entry) in fetched)
        {
            if (entry?.Ranked is { } ranked)
            {
                ranks[puuid] = ranked;
            }
        }

        return ranks;
    }

    private async Task<RankEntry?> RankFromRiotAsync(string platformId, string puuid, CancellationToken ct)
    {
        var key = $"live-rank:{puuid}";
        if (cache.TryGetValue(key, out RankEntry? cached))
        {
            return cached;
        }

        var result = await riot.GetLeagueEntriesAsync(platformId, puuid, ct);
        if (result.Outcome == RiotLiveOutcome.Unavailable)
        {
            // Not cached: a rank Riot could not give now is not "unranked".
            return null;
        }

        var solo = result.Value?.FirstOrDefault(e => e.QueueType == SoloQueueType && e.Tier is not null);
        var entry = new RankEntry(solo is null
            ? null
            : Ranked(solo.Tier!, solo.Rank ?? string.Empty, solo.LeaguePoints, solo.Wins, solo.Losses));
        return cache.Store(key, entry, RankTtl);
    }

    private static ProfileRankedReadModel Ranked(string tier, string division, int lp, int? wins, int? losses)
        => new()
        {
            Tier = tier,
            Division = division,
            LeaguePoints = lp,
            Wins = wins,
            Losses = losses,
            WinRate = wins is { } w && losses is { } l && w + l > 0 ? (double)w / (w + l) : null,
        };

    /// <summary><c>Name#TAG</c> → its halves; a game name may itself contain no <c>#</c>.</summary>
    private static (string? GameName, string? TagLine) SplitRiotId(string? riotId)
    {
        if (string.IsNullOrWhiteSpace(riotId))
        {
            return (null, null);
        }

        var hash = riotId.LastIndexOf('#');
        return hash < 0 ? (riotId, null) : (riotId[..hash], riotId[(hash + 1)..]);
    }
}
