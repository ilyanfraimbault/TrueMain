namespace Ingestor.Riot;

/// <summary>
/// One Riot API method the ingestor calls, named by Riot's own method id
/// (<c>match-v5.match</c>, …): the key of its method rate-limit bucket and of its usage rollups
/// (#93, #1359). Every Riot request carries its endpoint from the client method that builds it
/// (<see cref="RiotCall"/>), so nothing downstream re-derives it from the URL.
/// </summary>
/// <param name="Method">The stable method id, persisted in the usage rollups.</param>
public sealed record RiotEndpoint(string Method)
{
    /// <summary><c>account-v1.byRiotId</c>.</summary>
    public static RiotEndpoint AccountByRiotId { get; } = new("account-v1.byRiotId");

    /// <summary><c>account-v1.byPuuid</c>.</summary>
    public static RiotEndpoint AccountByPuuid { get; } = new("account-v1.byPuuid");

    /// <summary><c>match-v5.matchIdsByPuuid</c>.</summary>
    public static RiotEndpoint MatchIdsByPuuid { get; } = new("match-v5.matchIdsByPuuid");

    /// <summary><c>match-v5.timeline</c>.</summary>
    public static RiotEndpoint MatchTimeline { get; } = new("match-v5.timeline");

    /// <summary><c>match-v5.match</c>.</summary>
    public static RiotEndpoint Match { get; } = new("match-v5.match");

    /// <summary><c>league-v4.challenger</c>.</summary>
    public static RiotEndpoint ChallengerLeague { get; } = new("league-v4.challenger");

    /// <summary><c>league-v4.grandmaster</c>.</summary>
    public static RiotEndpoint GrandmasterLeague { get; } = new("league-v4.grandmaster");

    /// <summary><c>league-v4.master</c>.</summary>
    public static RiotEndpoint MasterLeague { get; } = new("league-v4.master");

    /// <summary><c>league-v4.entriesByPuuid</c>.</summary>
    public static RiotEndpoint LeagueEntriesByPuuid { get; } = new("league-v4.entriesByPuuid");

    /// <summary><c>league-v4.entriesByDivision</c>.</summary>
    public static RiotEndpoint LeagueEntriesByDivision { get; } = new("league-v4.entriesByDivision");

    /// <summary><c>summoner-v4.byPuuid</c>.</summary>
    public static RiotEndpoint SummonerByPuuid { get; } = new("summoner-v4.byPuuid");

    /// <summary><c>summoner-v4.byId</c>.</summary>
    public static RiotEndpoint SummonerById { get; } = new("summoner-v4.byId");

    /// <summary><c>championMastery-v4.byPuuid</c>.</summary>
    public static RiotEndpoint ChampionMasteriesByPuuid { get; } = new("championMastery-v4.byPuuid");

    /// <summary>
    /// The key a request reaching the Riot handlers without a <see cref="RiotCall"/> is recorded
    /// under: none of ours, so it shows up as something to map instead of failing the call.
    /// </summary>
    public const string Unknown = "unknown";

    /// <inheritdoc />
    public override string ToString() => Method;
}

/// <summary>
/// What a Riot request is: its <see cref="RiotEndpoint"/> and the routing value (<c>europe</c>,
/// <c>euw1</c>, …) whose host it goes to. Attached to the request by <see cref="Get"/>, the one
/// builder every client uses, and read back by the rate limiter and the usage metrics. Request
/// options travel with the request through the resilience pipeline's retries.
/// </summary>
public sealed record RiotCall(RiotEndpoint Endpoint, string RoutingValue)
{
    private static readonly HttpRequestOptionsKey<RiotCall> Key = new("TrueMain.RiotCall");

    /// <summary>
    /// A GET of <paramref name="pathAndQuery"/> on the host of <paramref name="routingValue"/>,
    /// carrying its endpoint.
    /// </summary>
    public static HttpRequestMessage Get(RiotEndpoint endpoint, string routingValue, string pathAndQuery)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{routingValue}.api.riotgames.com{pathAndQuery}"));
        request.Options.Set(Key, new RiotCall(endpoint, routingValue));
        return request;
    }

    /// <summary>The call <paramref name="request"/> was built for, or null when it was not built by <see cref="Get"/>.</summary>
    public static RiotCall? Of(HttpRequestMessage request)
        => request.Options.TryGetValue(Key, out var call) ? call : null;
}
