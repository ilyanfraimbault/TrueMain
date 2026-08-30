namespace Data.Entities;

public class MainChampionStat
{
    public Guid Id { get; set; }

    public string PlatformId { get; set; } = string.Empty;

    public string Puuid { get; set; } = string.Empty;

    public int ChampionId { get; set; }

    public int TotalMatches { get; set; }

    public int ChampionMatches { get; set; }

    public double PlayRate { get; set; }

    public bool IsMain { get; set; }

    /// <summary>
    /// False when <c>MainActivityProcess</c> saw, through champion mastery
    /// <c>lastPlayTime</c>, that the player stopped playing this champion
    /// (#900). The row is kept — history stays readable and a returning player
    /// is reactivated without going through discovery again — but an inactive
    /// row is excluded from the leaderboard, from the coverage counts and from
    /// the match-ingestion queue, so a dead main stops consuming match-v5 calls.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True when the last analysis cycle found <b>no</b> match participants for
    /// this account at all, so the numbers on this row describe a sample that no
    /// longer exists (#1216). That happens on its own: raw matches age out of
    /// <c>MatchDataRetention</c> — two patches in prod — and an account nobody
    /// re-ingested drops to zero participants.
    ///
    /// Distinct from <see cref="IsActive"/>, which asks whether the *player*
    /// still plays the champion (Riot mastery <c>lastPlayTime</c>). A row can
    /// easily be active and retired at once: they still main it, we just no
    /// longer hold the games.
    ///
    /// The row is deliberately kept rather than deleted — dropping it would take
    /// the player off the leaderboard the moment their matches expire — but
    /// readers must present its figures as historical, dated by
    /// <see cref="CalculatedAtUtc"/>, not as current. Self-clearing: the next
    /// cycle that sees real games writes false.
    /// </summary>
    public bool IsSampleRetired { get; set; }

    public bool IsOtp { get; set; }

    /// <summary>
    /// True when this row is a main only thanks to the coverage-adaptive threshold
    /// (its play rate is below the base MainAnalysis play-rate threshold). Lets the UI
    /// label these as an "extended sample" for under-covered champions.
    /// </summary>
    public bool IsExtendedSample { get; set; }

    public string PrimaryPosition { get; set; } = string.Empty;

    public List<PositionStat> PositionBreakdown { get; set; } = new();

    public DateTime CalculatedAtUtc { get; set; }
}

public class PositionStat
{
    public string Position { get; set; } = string.Empty;
    public int Games { get; set; }
    public double Rate { get; set; }
}
