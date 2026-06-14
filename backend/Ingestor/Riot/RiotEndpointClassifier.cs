namespace Ingestor.Riot;

/// <summary>
/// Maps a Riot API request URI to a stable, low-cardinality endpoint key (the
/// Riot "method" id, e.g. <c>match-v5.getMatch</c>) and its routing host, so the
/// usage metrics (#93) group calls cleanly instead of exploding on the puuids,
/// match ids and queue ids embedded in the path.
/// </summary>
/// <remarks>
/// Deliberately uses ordered segment checks rather than regex: the path shapes
/// are fixed and few, so a string match-chain is both faster and avoids the
/// source-generated-regex analyzer churn. Most-specific patterns are tested
/// first (e.g. <c>/matches/.../ids</c> and <c>/matches/.../timeline</c> before
/// the bare <c>/matches/{id}</c>).
/// </remarks>
internal static class RiotEndpointClassifier
{
    /// <summary>
    /// Classifies a request into its endpoint key and routing host. Returns
    /// <c>("unknown", host)</c> for an unrecognised path so a newly added Riot
    /// call still records (and shows up as something to map) rather than throwing.
    /// </summary>
    public static (string Endpoint, string? Route) Classify(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null)
        {
            return ("unknown", null);
        }

        var route = RouteFromHost(uri.Host);
        return (EndpointFromPath(uri.AbsolutePath), route);
    }

    private static string EndpointFromPath(string path)
    {
        // account-v1 (regional)
        if (path.Contains("/accounts/by-riot-id/", StringComparison.Ordinal))
        {
            return "account-v1.byRiotId";
        }

        if (path.Contains("/accounts/by-puuid/", StringComparison.Ordinal))
        {
            return "account-v1.byPuuid";
        }

        // match-v5 (regional) — order matters: the ids/timeline sub-resources must
        // be tested before the bare /matches/{id}.
        if (path.Contains("/matches/by-puuid/", StringComparison.Ordinal))
        {
            return "match-v5.matchIdsByPuuid";
        }

        if (path.StartsWith("/lol/match/v5/matches/", StringComparison.Ordinal)
            && path.EndsWith("/timeline", StringComparison.Ordinal))
        {
            return "match-v5.timeline";
        }

        if (path.StartsWith("/lol/match/v5/matches/", StringComparison.Ordinal))
        {
            return "match-v5.match";
        }

        // league-v4 (platform)
        if (path.Contains("/challengerleagues/", StringComparison.Ordinal))
        {
            return "league-v4.challenger";
        }

        if (path.Contains("/grandmasterleagues/", StringComparison.Ordinal))
        {
            return "league-v4.grandmaster";
        }

        if (path.Contains("/masterleagues/", StringComparison.Ordinal))
        {
            return "league-v4.master";
        }

        if (path.Contains("/league/v4/entries/by-puuid/", StringComparison.Ordinal))
        {
            return "league-v4.entriesByPuuid";
        }

        // summoner-v4 (platform)
        if (path.Contains("/summoners/by-puuid/", StringComparison.Ordinal))
        {
            return "summoner-v4.byPuuid";
        }

        if (path.StartsWith("/lol/summoner/v4/summoners/", StringComparison.Ordinal))
        {
            return "summoner-v4.byId";
        }

        // champion-mastery-v4 (platform)
        if (path.Contains("/champion-masteries/by-puuid/", StringComparison.Ordinal))
        {
            return "championMastery-v4.byPuuid";
        }

        return "unknown";
    }

    /// <summary>
    /// Extracts the routing label from a Riot host: the first DNS label of e.g.
    /// <c>europe.api.riotgames.com</c> → <c>europe</c> or
    /// <c>euw1.api.riotgames.com</c> → <c>euw1</c>. Returns null when the host has
    /// no leading label.
    /// </summary>
    private static string? RouteFromHost(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        var dot = host.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? host[..dot] : host;
    }
}
