using TrueMain.ReadModels.Truemains;

namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Head-to-head read model returned by
/// <c>GET /champions/{championId}/mains-comparison</c> (issue #528): one Riot
/// account's numbers on a champion set against the champion's mains — either
/// the whole main pool (<see cref="ChampionComparisonSideReadModel.Identity"/>
/// null) or a single named main.
///
/// Both sides are computed from the same population, queue, patch and lane
/// scope, so the two columns are directly comparable. Build / skill order and
/// early-game leads are deliberately out of scope here (a separate issue
/// covers them).
///
/// The endpoint never reaches out to Riot: the account must already exist in
/// our database, and an unknown Riot ID resolves to
/// <see cref="ChampionComparisonStatus.UnknownAccount"/> rather than an error.
/// </summary>
public sealed record ChampionMainsComparisonResponse
{
    public int ChampionId { get; init; }

    /// <summary>
    /// Resolved patch (<c>major.minor</c>) both sides were computed for, or
    /// <see langword="null"/> when the caller pinned none and the slice spans
    /// every patch still held by retention.
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// Riot team position both sides were narrowed to, or <see langword="null"/>
    /// when the comparison spans every lane.
    /// </summary>
    public string? Position { get; init; }

    /// <summary>
    /// Games each side needs before the comparison is considered meaningful
    /// (<c>ChampionsList:MinComparisonGames</c>). Exposed so the caller can say
    /// how far a thin sample is from the bar instead of just hiding the panel.
    /// </summary>
    public int MinGames { get; init; }

    /// <summary>Outcome of the lookup — see <see cref="ChampionComparisonStatus"/>.</summary>
    public string Status { get; init; } = ChampionComparisonStatus.Ok;

    /// <summary>
    /// The compared account's side. Null only when the Riot ID is unknown to us
    /// (<see cref="ChampionComparisonStatus.UnknownAccount"/>) — a known account
    /// with no games on the champion still yields a side with zero games.
    /// </summary>
    public ChampionComparisonSideReadModel? Player { get; init; }

    /// <summary>
    /// The yardstick column: by default the aggregate of every tracked main of
    /// this champion, excluding the compared account itself.
    ///
    /// A <c>main</c> target narrows it to that one account's games on the
    /// champion. The target is resolved as <em>any</em> account we hold — it is
    /// deliberately not required to be flagged a main of this champion, so a
    /// caller can measure themselves against a specific rival. Only the default
    /// (pool) column is restricted to actual mains. The UI only ever offers
    /// real mains as targets.
    ///
    /// Targeting the compared account itself is allowed and answered honestly:
    /// both columns describe the same games, so they are identical and every
    /// delta is zero. That is the arithmetic being right, not a special case —
    /// it earns no guard and no status of its own. The picker drops the
    /// compared account from its options so the UI never leads anyone into it.
    ///
    /// Null when the account is unknown, or when a targeted account is.
    /// </summary>
    public ChampionComparisonSideReadModel? Mains { get; init; }
}

/// <summary>
/// Why a comparison did — or did not — produce two comparable columns. A
/// string rather than an enum so the JSON contract stays readable and the
/// frontend can branch on it without a numeric mapping.
/// </summary>
public static class ChampionComparisonStatus
{
    /// <summary>Both sides cleared the games floor; the columns are comparable.</summary>
    public const string Ok = "OK";

    /// <summary>
    /// The Riot ID is not in our database. Deliberately not a 404: this is the
    /// expected outcome for any account we have never ingested, and the caller
    /// renders an explanatory empty state rather than an error.
    /// </summary>
    public const string UnknownAccount = "UNKNOWN_ACCOUNT";

    /// <summary>
    /// The targeted <c>main</c>'s Riot ID is not in our database. The compared
    /// account resolved fine, so
    /// <see cref="ChampionMainsComparisonResponse.Player"/> is still populated —
    /// only the yardstick is missing.
    /// </summary>
    public const string UnknownTarget = "UNKNOWN_TARGET";

    /// <summary>
    /// At least one side is below <see cref="ChampionMainsComparisonResponse.MinGames"/>.
    /// Both sides are still returned (with their real game counts) so the caller
    /// can say which one is thin.
    /// </summary>
    public const string InsufficientSample = "INSUFFICIENT_SAMPLE";
}

/// <summary>
/// One column of the comparison. Counting stats are per-game averages so a
/// single player and a pool of mains stay on the same scale.
/// </summary>
public sealed record ChampionComparisonSideReadModel
{
    /// <summary>
    /// Riot identity of this side; <see langword="null"/> for the aggregate of
    /// the champion's mains, which has no single owner.
    /// </summary>
    public ProfileIdentityReadModel? Identity { get; init; }

    /// <summary>
    /// Distinct accounts contributing to the column — always 1 for a single
    /// player, the size of the main pool for the aggregate.
    /// </summary>
    public int Players { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    /// <summary><c>wins / games</c>; 0 when the side has no games.</summary>
    public double WinRate { get; init; }

    /// <summary>Kills per game.</summary>
    public double Kills { get; init; }

    /// <summary>Deaths per game.</summary>
    public double Deaths { get; init; }

    /// <summary>Assists per game.</summary>
    public double Assists { get; init; }

    /// <summary>
    /// <c>(kills + assists) / deaths</c>, falling back to <c>(kills + assists) / games</c>
    /// on a deathless sample so it stays on the same per-game scale as the metrics beside
    /// it — the same convention the truemains leaderboard uses.
    /// </summary>
    public double Kda { get; init; }

    /// <summary>
    /// Minions + neutral monsters per minute of game time, computed over the
    /// summed durations so long games weigh proportionally.
    /// </summary>
    public double CsPerMin { get; init; }

    /// <summary>Gold earned per minute of game time, same denominator as <see cref="CsPerMin"/>.</summary>
    public double GoldPerMin { get; init; }

    /// <summary>Gold earned per game.</summary>
    public double GoldPerGame { get; init; }

    /// <summary>Whether <see cref="Games"/> reached <see cref="ChampionMainsComparisonResponse.MinGames"/>.</summary>
    public bool SampleMet { get; init; }
}
