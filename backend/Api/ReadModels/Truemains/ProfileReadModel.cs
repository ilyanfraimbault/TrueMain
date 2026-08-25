namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// One truemain profile (<c>GET /truemains/{nameTag}/profile</c>) — the
/// payload behind the profile page. Composes identity (from
/// <c>RiotAccount</c>), latest ranked snapshot (from <c>RankSnapshot</c>),
/// the player's main champions (from <c>MainChampionStat</c> where
/// <c>IsMain=true</c>), and an aggregated position breakdown summed across
/// those mains.
/// </summary>
public sealed record ProfileReadModel
{
    public ProfileIdentityReadModel Identity { get; init; } = new();

    /// <summary>Null when the player has no ranked snapshot yet (unranked or not refreshed).</summary>
    public ProfileRankedReadModel? Ranked { get; init; }

    public IReadOnlyList<ProfileMainChampionReadModel> Mains { get; init; }
        = Array.Empty<ProfileMainChampionReadModel>();

    /// <summary>
    /// How devoted the player is to their signature champion (their top main),
    /// 0..100 with the full component breakdown. Null when the player has no
    /// champion classified as a main yet — the profile hides the card rather
    /// than showing a zero that would read as "not dedicated".
    /// </summary>
    public DedicationReadModel? Dedication { get; init; }

    /// <summary>
    /// Account-level position distribution, summed across the player's main
    /// champions. Each entry covers TOP / JUNGLE / MIDDLE / BOTTOM / UTILITY
    /// (Riot strings, uppercase). Always present, possibly empty when the
    /// player has no main champions tracked yet.
    /// </summary>
    public IReadOnlyList<ProfilePositionStatReadModel> Positions { get; init; }
        = Array.Empty<ProfilePositionStatReadModel>();
}

public sealed record ProfileIdentityReadModel
{
    public string GameName { get; init; } = string.Empty;

    /// <summary>Riot tag line (Riot ID suffix). Null when the row was ingested before tag lines were stored.</summary>
    public string? TagLine { get; init; }

    public string PlatformId { get; init; } = string.Empty;

    public int ProfileIconId { get; init; }

    public int SummonerLevel { get; init; }
}

public sealed record ProfileRankedReadModel
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

public sealed record ProfileMainChampionReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    /// <summary><c>games / total games on the account</c> as stored by the main analysis (0..1).</summary>
    public double PlayRate { get; init; }

    /// <summary>Riot team position string (uppercase, e.g. <c>MIDDLE</c>). Empty when no dominant lane.</summary>
    public string PrimaryPosition { get; init; } = string.Empty;

    public bool IsOtp { get; init; }

    /// <summary>
    /// True when the matches these figures were computed from have aged out of
    /// retention, so <see cref="Games"/> and <see cref="PlayRate"/> describe a
    /// sample the site no longer holds (#1216). The row is kept on purpose — the
    /// player stays on the leaderboard — but the UI must date it with
    /// <see cref="MeasuredAtUtc"/> rather than present it as current.
    /// </summary>
    public bool IsSampleRetired { get; init; }

    /// <summary>When the main analysis last recomputed these figures.</summary>
    public DateTime MeasuredAtUtc { get; init; }
}

public sealed record ProfilePositionStatReadModel
{
    public string Position { get; init; } = string.Empty;

    public int Games { get; init; }

    /// <summary><c>games / sum(games) across the player's mains</c> (0..1).</summary>
    public double Rate { get; init; }
}
