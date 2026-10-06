namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Answer of <c>GET /truemains/lookup</c> (#1910): of the players asked about,
/// the ones who are true mains of the champion they were asked with. A player
/// who is not is simply absent, so the response says nothing about anyone else.
/// </summary>
public sealed record TruemainLookupResponse
{
    public IReadOnlyList<TruemainLookupEntryReadModel> Players { get; init; } = [];
}

/// <summary>
/// One player found to be a true main of the champion they were asked with:
/// the figures the desktop app's mark explains itself with, all read off the
/// same <c>main_champion_stats</c> row the leaderboard admits them on.
/// </summary>
public sealed record TruemainLookupEntryReadModel
{
    /// <summary>The Riot ID as TrueMain stores it, <c>Name#TAG</c>; match it case-insensitively.</summary>
    public string RiotId { get; init; } = string.Empty;

    /// <summary>The <c>{gameName}-{tagLine}</c> slug of the player's profile.</summary>
    public string NameTag { get; init; } = string.Empty;

    /// <summary>The champion they were asked with, which they main.</summary>
    public int ChampionId { get; init; }

    /// <summary>Recent ranked games on the champion.</summary>
    public int ChampionMatches { get; init; }

    /// <summary>Recent ranked games the share is measured over (capped by main analysis).</summary>
    public int TotalMatches { get; init; }

    /// <summary><see cref="ChampionMatches"/> over <see cref="TotalMatches"/>, 0..1.</summary>
    public double PlayRate { get; init; }

    /// <summary>A one-trick on the champion: the same flag as the OTP badge.</summary>
    public bool IsOtp { get; init; }

    /// <summary>Riot champion-mastery points on the champion; null until read.</summary>
    public long? MasteryPoints { get; init; }

    /// <summary>The Truemain score on this champion, 0..100 (<see cref="DedicationReadModel.Score"/>).</summary>
    public double Dedication { get; init; }
}
