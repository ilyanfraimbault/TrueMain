namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// TrueMain's signature metric for a player's signature champion — shown as the
/// <em>Truemain score</em>; the wire name stays <c>dedication</c> so existing
/// links (<c>?sort=dedication</c>) keep working (#1701). Ships the raw facts and
/// each part's contribution in score points, so the UI explains the number with
/// figures that add up instead of asserting it — the formula lives in
/// <see cref="Core.Truemains.DedicationScore"/> and is documented in
/// <c>docs/dedication-score.md</c>.
/// </summary>
public sealed record DedicationReadModel
{
    /// <summary>Final score, 0..100 (one decimal). The sum of <see cref="Parts"/>' points.</summary>
    public double Score { get; init; }

    /// <summary>The champion the score is about: the player's most-played main (their signature champion), or the filtered one.</summary>
    public int ChampionId { get; init; }

    /// <summary>
    /// The verdict: true when the player is a one-trick on <see cref="ChampionId"/>.
    /// The same <c>main_champion_stats.IsOtp</c> flag the OTP badge and the
    /// <c>otpOnly</c> filter read, so the three can never disagree.
    /// </summary>
    public bool IsOtp { get; init; }

    /// <summary>Raw share of the player's recent ranked games spent on the champion (0..1).</summary>
    public double PlayRate { get; init; }

    /// <summary>Recent ranked games on the champion — the numerator of <see cref="PlayRate"/>.</summary>
    public int ChampionGames { get; init; }

    /// <summary>Recent ranked games the play rate is measured over — its denominator.</summary>
    public int RecentGames { get; init; }

    /// <summary>Riot champion-mastery points on the champion. Null until the mastery has been read.</summary>
    public long? MasteryPoints { get; init; }

    /// <summary>1-based rank of the champion in the player's mastery by points. Null until read, or when Riot has no entry for it.</summary>
    public int? MasteryRank { get; init; }

    /// <summary>Whole days since the player last played the champion, per Riot mastery. Null until read.</summary>
    public int? DaysSinceLastPlayed { get; init; }

    /// <summary>Each part's contribution, in the order the UI lists them (heaviest first).</summary>
    public IReadOnlyList<DedicationPartReadModel> Parts { get; init; } = [];
}

/// <summary>One part of the score and what it contributed.</summary>
public sealed record DedicationPartReadModel
{
    /// <summary>One of <see cref="DedicationPartKeys"/>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Score points this part contributed (unrounded; the parts sum to the score).</summary>
    public double Points { get; init; }

    /// <summary>Score points this part can contribute at most — its weight × 100.</summary>
    public double MaxPoints { get; init; }
}

/// <summary>Wire keys of <see cref="DedicationPartReadModel.Key"/>, mirrored by <c>web/shared/types/dedication.ts</c>.</summary>
public static class DedicationPartKeys
{
    public const string PlayRate = "playRate";
    public const string Mastery = "mastery";
    public const string MasteryRank = "masteryRank";
}
