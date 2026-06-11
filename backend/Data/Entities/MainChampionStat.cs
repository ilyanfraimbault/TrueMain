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
