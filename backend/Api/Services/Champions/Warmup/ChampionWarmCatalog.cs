using System.Globalization;
using Core.Lol.Ranking;

namespace TrueMain.Services.Champions.Warmup;

/// <summary>One line of the directory: a champion on a lane.</summary>
public sealed record ChampionWarmLine(int ChampionId, string Position);

/// <summary>
/// What a warm pass requests. These are the URLs the site really sends — the shape of each is
/// the web app's, so the entries they fill are the ones a visitor will read — rather than calls
/// into the query services, where a difference in how a parameter is normalised would warm an
/// entry nobody ever hits. Replaying them also warms what a direct call would not: the routing,
/// the serializers and the first run of each query.
/// </summary>
public static class ChampionWarmCatalog
{
    private const int DirectoryPageSize = 50;

    private static readonly string[] Positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    private static readonly string[] LeaderboardSorts = ["dedication", "games", "kda", "winRate"];

    private static readonly string[] LeaderboardRegions = ["europe", "americas", "korea"];

    /// <summary>
    /// Every rank filter the site offers: all ranks, each tier and each "tier and above".
    /// Challenger tops the ladder, so its "and above" would be the same read under another key.
    /// </summary>
    public static IReadOnlyList<string> RankFilters { get; } =
    [
        EloBracket.All,
        .. EloBracket.Ladder
            .Where(tier => tier != EloBracket.Challenger)
            .Select(tier => tier + EloBracket.PlusSuffix),
        .. EloBracket.Ladder,
    ];

    /// <summary>
    /// The directory and the tier list, for every rank and population of each patch. These are
    /// the reads that cost seconds when cold, so they come first; the rest only reshape what
    /// the first request of each slice has computed.
    /// </summary>
    public static IReadOnlyList<string> Directory(IReadOnlyList<string> patchesNewestFirst)
    {
        var urls = new List<string>();
        foreach (var patch in patchesNewestFirst)
        {
            foreach (var rank in RankFilters)
            {
                urls.Add(DirectoryPage(patch, rank, truemainsOnly: true));
                urls.Add(DirectoryPage(patch, rank, truemainsOnly: false));
            }
        }

        foreach (var rank in RankFilters)
        {
            foreach (var truemainsOnly in new[] { true, false })
            {
                var population = truemainsOnly ? string.Empty : "&truemainsOnly=false";
                urls.Add($"/champions/directory?pageSize={DirectoryPageSize}&eloBracket={rank}{population}");
                urls.Add($"/champions/tierlist?eloBracket={rank}{population}");
                foreach (var patch in patchesNewestFirst)
                {
                    urls.Add($"/champions/tierlist?patch={patch}&eloBracket={rank}{population}");
                }

                foreach (var position in Positions)
                {
                    urls.Add($"/champions/directory?pageSize={DirectoryPageSize}&position={position}&eloBracket={rank}{population}");
                    urls.Add($"/champions/tierlist?position={position}&eloBracket={rank}{population}");
                }
            }
        }

        urls.Add("/champions/overview");
        return urls;
    }

    /// <summary>
    /// A champion page's reads, as the page opens: on <paramref name="rank"/>, on the active
    /// patch, and for each of the champion's lines. A rank or a patch the visitor picks after
    /// that is read on demand, which does not touch the directory's cost.
    /// </summary>
    public static IReadOnlyList<string> ChampionPages(
        IReadOnlyList<ChampionWarmLine> lines, string activePatch, string rank)
    {
        var urls = new List<string>();
        foreach (var championId in lines.Select(line => line.ChampionId).Distinct())
        {
            var id = championId.ToString(CultureInfo.InvariantCulture);
            urls.Add($"/champions/{id}?eloBracket={rank}&truemainsOnly=true");
            urls.Add($"/champions/{id}/trend?eloBracket={rank}");
        }

        foreach (var line in lines)
        {
            var id = line.ChampionId.ToString(CultureInfo.InvariantCulture);
            var lane = $"position={line.Position}";
            var slice = $"{lane}&patch={activePatch}&eloBracket={rank}";
            urls.Add($"/champions/{id}?{lane}&eloBracket={rank}&truemainsOnly=true");
            urls.Add($"/champions/{id}?{slice}&truemainsOnly=true");
            urls.Add($"/champions/{id}/trend?{lane}&eloBracket={rank}");
            urls.Add($"/champions/directory?pageSize=1&championId={id}&{slice}");
            urls.Add($"/champions/{id}/scaling?{slice}");
            urls.Add($"/champions/{id}/item-context?{lane}&patch={activePatch}");
            urls.Add($"/champions/{id}/matchups?{lane}&eloBracket={rank}");
            urls.Add($"/champions/{id}/matchups?{slice}");
            urls.Add($"/champions/{id}/synergies?{lane}&eloBracket={rank}&patch={activePatch}");
        }

        return urls;
    }

    /// <summary>
    /// The leaderboard: its count on each filter a visitor reaches in one click, and its whole
    /// ranking under each sort. A page of it is then a few indexed reads, whatever the
    /// 30 s response cache has let go.
    /// </summary>
    public static IReadOnlyList<string> Leaderboard()
    {
        var urls = new List<string>
        {
            "/truemains?page=1&pageSize=25",
            "/truemains?page=1&pageSize=25&otpOnly=true",
        };
        urls.AddRange(LeaderboardSorts.Select(sort => $"/truemains?page=1&pageSize=25&sort={sort}"));
        urls.AddRange(Positions.Select(position => $"/truemains?page=1&pageSize=25&position={position}"));
        urls.AddRange(LeaderboardRegions.Select(region => $"/truemains?page=1&pageSize=25&region={region}"));
        return urls;
    }

    private static string DirectoryPage(string patch, string rank, bool truemainsOnly)
        => $"/champions/directory?pageSize={DirectoryPageSize}&patch={patch}&eloBracket={rank}"
            + (truemainsOnly ? string.Empty : "&truemainsOnly=false");
}
