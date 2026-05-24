namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// One truemain profile (<c>GET /truemains/{nameTag}/profile</c>) — the
/// payload behind the profile page. Composes identity (from
/// <c>RiotAccount</c>), latest ranked snapshot (from <c>RankSnapshot</c>),
/// the player's main champions (from <c>MainChampionStat</c> where
/// <c>IsMain=true</c>), and an aggregated position breakdown summed across
/// those mains.
/// </summary>
public sealed class ProfileReadModel
{
    public ProfileIdentityReadModel Identity { get; init; } = new();

    /// <summary>Null when the player has no ranked snapshot yet (unranked or not refreshed).</summary>
    public ProfileRankedReadModel? Ranked { get; init; }

    public IReadOnlyList<ProfileMainChampionReadModel> Mains { get; init; }
        = Array.Empty<ProfileMainChampionReadModel>();

    /// <summary>
    /// Account-level position distribution, summed across the player's main
    /// champions. Each entry covers TOP / JUNGLE / MIDDLE / BOTTOM / UTILITY
    /// (Riot strings, uppercase). Always present, possibly empty when the
    /// player has no main champions tracked yet.
    /// </summary>
    public IReadOnlyList<ProfilePositionStatReadModel> Positions { get; init; }
        = Array.Empty<ProfilePositionStatReadModel>();
}

public sealed class ProfileIdentityReadModel
{
    public string GameName { get; init; } = string.Empty;

    /// <summary>Riot tag line (Riot ID suffix). Null when the row was ingested before tag lines were stored.</summary>
    public string? TagLine { get; init; }

    public string PlatformId { get; init; } = string.Empty;

    public int ProfileIconId { get; init; }

    public int SummonerLevel { get; init; }
}

public sealed class ProfileRankedReadModel
{
    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }

    /// <summary>Wins this split (nullable when Riot's league response omitted it).</summary>
    public int? Wins { get; init; }

    /// <summary>Losses this split (nullable when Riot's league response omitted it).</summary>
    public int? Losses { get; init; }

    /// <summary>
    /// <c>wins / (wins + losses)</c> when both are present, otherwise null.
    /// The frontend hides the winrate label when null instead of rendering
    /// 0% or NaN.
    /// </summary>
    public double? WinRate { get; init; }
}

public sealed class ProfileMainChampionReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    /// <summary><c>games / total games on the account</c> as stored by the main analysis (0..1).</summary>
    public double PlayRate { get; init; }

    /// <summary>Riot team position string (uppercase, e.g. <c>MIDDLE</c>). Empty when no dominant lane.</summary>
    public string PrimaryPosition { get; init; } = string.Empty;

    public bool IsOtp { get; init; }
}

public sealed class ProfilePositionStatReadModel
{
    public string Position { get; init; } = string.Empty;

    public int Games { get; init; }

    /// <summary><c>games / sum(games) across the player's mains</c> (0..1).</summary>
    public double Rate { get; init; }
}
