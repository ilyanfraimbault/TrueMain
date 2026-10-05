using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Lookup;

/// <summary>
/// One player asked about: a Riot ID, lowered, and the champion they are on.
/// </summary>
public sealed record TruemainLookupPair(string GameNameLower, string TagLineLower, int ChampionId);

/// <summary>
/// The parsed query of <c>GET /truemains/lookup</c> (#1910): the platform the
/// game is played on and up to <see cref="MaxPlayers"/> players, each sent as
/// <c>player=Name#TAG:championId</c>.
/// </summary>
/// <remarks>
/// The pairs come out lowered, de-duplicated and sorted, so two requests that
/// ask about the same game in another order or casing share one <see cref="Key"/>
/// — the cache key, and what the ten app instances of one game coalesce on.
/// </remarks>
public sealed record TruemainLookupRequest(string PlatformId, IReadOnlyList<TruemainLookupPair> Pairs)
{
    /// <summary>The players of one game.</summary>
    public const int MaxPlayers = 10;

    // Riot platform ids are short alphanumeric strings (EUW1, KR, OC1).
    private const int MaxPlatformLength = 8;

    /// <summary>Lowered, sorted pairs plus the platform: the same game asks the same key.</summary>
    public string Key => $"{PlatformId}|{string.Join(';', Pairs.Select(p => $"{p.GameNameLower}#{p.TagLineLower}:{p.ChampionId}"))}";

    /// <summary>
    /// Parses the query, or returns the reason it is not one — the controller's
    /// 400. Every player must be a well-formed Riot ID (<see cref="NameTagParser.TryParseRiotId"/>)
    /// and a positive champion id, separated by the last <c>:</c>.
    /// </summary>
    public static bool TryParse(
        string? platformId,
        IReadOnlyList<string>? players,
        out TruemainLookupRequest request,
        out string error)
    {
        request = new TruemainLookupRequest(string.Empty, []);
        error = string.Empty;

        var platform = platformId?.Trim().ToUpperInvariant() ?? string.Empty;
        if (platform.Length is 0 or > MaxPlatformLength || !platform.All(char.IsAsciiLetterOrDigit))
        {
            error = "platformId must be a Riot platform id, such as EUW1.";
            return false;
        }

        if (players is null || players.Count == 0)
        {
            error = "At least one player is required.";
            return false;
        }

        if (players.Count > MaxPlayers)
        {
            error = $"At most {MaxPlayers} players can be looked up at once.";
            return false;
        }

        var pairs = new SortedSet<TruemainLookupPair>(PairOrder.Instance);
        foreach (var player in players)
        {
            if (!TryParsePlayer(player, out var pair))
            {
                error = "Each player must be Name#TAG:championId, with a positive champion id.";
                return false;
            }

            pairs.Add(pair);
        }

        request = new TruemainLookupRequest(platform, [.. pairs]);
        return true;
    }

    private static bool TryParsePlayer(string? player, out TruemainLookupPair pair)
    {
        pair = new TruemainLookupPair(string.Empty, string.Empty, 0);
        var separator = player?.LastIndexOf(':') ?? -1;
        if (separator <= 0)
        {
            return false;
        }

        // Digits only: int.TryParse alone would let a sign or spaces through.
        var championText = player![(separator + 1)..];
        if (championText.Length is 0 or > 9
            || !championText.All(char.IsAsciiDigit)
            || !int.TryParse(championText, out var championId)
            || championId <= 0)
        {
            return false;
        }

        if (!NameTagParser.TryParseRiotId(player[..separator], out var riotId))
        {
            return false;
        }

        // Lowered here, matched against lower("GameName") / lower("TagLine") in
        // SQL — the same rule as TruemainAccountResolver.
        pair = new TruemainLookupPair(riotId.GameName.ToLowerInvariant(), riotId.TagLine.ToLowerInvariant(), championId);
        return true;
    }

    private sealed class PairOrder : IComparer<TruemainLookupPair>
    {
        public static readonly PairOrder Instance = new();

        public int Compare(TruemainLookupPair? x, TruemainLookupPair? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var byName = string.CompareOrdinal(x.GameNameLower, y.GameNameLower);
            if (byName != 0)
            {
                return byName;
            }

            var byTag = string.CompareOrdinal(x.TagLineLower, y.TagLineLower);
            return byTag != 0 ? byTag : x.ChampionId.CompareTo(y.ChampionId);
        }
    }
}
