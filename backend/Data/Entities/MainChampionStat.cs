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
