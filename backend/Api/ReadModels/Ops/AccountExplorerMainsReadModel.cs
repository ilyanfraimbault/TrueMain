namespace TrueMain.ReadModels.Ops;

/// <summary>The main-champion rows and the thresholds that decided them.</summary>
public sealed record AccountExplorerMainsReadModel
{
    /// <summary>Rows for the account, highest play rate first.</summary>
    public IReadOnlyList<AccountExplorerMainRowReadModel> Rows { get; init; } = [];

    public AccountExplorerMainThresholdsReadModel Thresholds { get; init; } = new();
}

/// <summary>
/// The configured <c>MainAnalysis</c> thresholds, so a row's verdict can be read
/// against the rule that produced it.
/// </summary>
public sealed record AccountExplorerMainThresholdsReadModel
{
    /// <summary>
    /// Base play rate required to be a main for a well-covered champion (0.20).
    /// </summary>
    public double PlayRateThreshold { get; init; }

    /// <summary>
    /// Lowest the adaptive threshold can drop to, for a maximally under-covered
    /// champion (0.12, #407).
    /// </summary>
    public double PlayRateFloor { get; init; }

    public double OtpPlayRateThreshold { get; init; }

    /// <summary>
    /// Below this many analysed matches, <c>MainAnalysis</c> refuses to overwrite
    /// an account that already has an established main (#825).
    /// </summary>
    public int MinMatchesToEvaluate { get; init; }

    /// <summary>
    /// Why only a band is given: the effective per-champion threshold interpolates
    /// between the floor and the base threshold according to a live
    /// champion-coverage snapshot that is computed inside the Ingestor and never
    /// persisted. Naming an exact number here would be an invention.
    /// </summary>
    public string EffectiveThresholdNote { get; init; } = string.Empty;
}

/// <summary>One <c>main_champion_stats</c> row.</summary>
public sealed record AccountExplorerMainRowReadModel
{
    public int ChampionId { get; init; }

    /// <summary>Matches the analysis pass looked at (its sample size), not the account's total.</summary>
    public int TotalMatches { get; init; }

    public int ChampionMatches { get; init; }

    public double PlayRate { get; init; }

    public bool IsMain { get; init; }

    public bool IsOtp { get; init; }

    /// <summary>
    /// A main only thanks to the coverage-relaxed floor: its play rate sits below
    /// the base threshold (#407).
    /// </summary>
    public bool IsExtendedSample { get; init; }

    public bool IsActive { get; init; }

    public string PrimaryPosition { get; init; } = string.Empty;

    public IReadOnlyList<AccountExplorerPositionStatReadModel> PositionBreakdown { get; init; } = [];

    public DateTime CalculatedAtUtc { get; init; }

    /// <summary>
    /// True when the account's last <c>MainAnalysis</c> run is newer than this
    /// row's own <c>CalculatedAtUtc</c>: the process looked at the account and
    /// declined to overwrite — the thin-sample guard (#825). Not a stale-data bug.
    /// </summary>
    public bool AnalysisSkipped { get; init; }

    /// <summary>Null while the row is active.</summary>
    public AccountExplorerDeactivationReadModel? Deactivation { get; init; }
}

/// <summary>
/// What is knowable about a deactivated main row — which is less than one would
/// like, and this record says so rather than guessing.
/// </summary>
public sealed record AccountExplorerDeactivationReadModel
{
    /// <summary>
    /// The account's last successful mastery check. Deactivation is only
    /// trustworthy alongside this: a failed lookup leaves both the flag and the
    /// stamp untouched, so a null here means the retirement was never confirmed
    /// by a completed check.
    /// </summary>
    public DateTime? ConfirmedByActivityCheckAtUtc { get; init; }

    /// <summary>
    /// Always false: there is no retirement-reason column.
    /// <c>MainActivityProcess</c> writes the boolean and nothing else.
    /// </summary>
    public bool ReasonKnown { get; init; }

    /// <summary>The two causes the boolean collapses together, spelled out.</summary>
    public string ReasonNote { get; init; } = string.Empty;
}

/// <summary>One lane of a main row's position breakdown.</summary>
public sealed record AccountExplorerPositionStatReadModel
{
    public string Position { get; init; } = string.Empty;

    public int Games { get; init; }

    public double Rate { get; init; }
}
