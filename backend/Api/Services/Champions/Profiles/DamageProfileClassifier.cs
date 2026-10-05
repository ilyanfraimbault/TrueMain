using Data.Entities;
using Data.ItemContext;
using Data.Statics;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Profiles;

/// <summary>
/// The arithmetic of the damage-profile endpoint (#1905): shares from the additive sums,
/// the build split and its flex flag, and the static class of an unprofiled champion. Pure,
/// so the rules are tested without a database.
/// </summary>
public static class DamageProfileClassifier
{
    /// <summary>
    /// Share of a champion's archetype games each of two opposite builds must hold for the
    /// champion to count as going both ways. Not a per-champion label: one cut for every
    /// champion, written here. A fifth of the games is where an off-build stops being the
    /// odd game and becomes a pick the draft has to plan for — below it the blended profile
    /// is already the honest answer.
    /// </summary>
    public const double FlexMinShare = 0.2;

    /// <summary>
    /// How far apart Data Dragon's 0–10 attack and magic ratings must be for a fallback to
    /// name one damage type; closer than this the champion is <c>mixed</c>.
    /// </summary>
    public const int FallbackRatingGap = 3;

    public const string Physical = "physical";

    public const string Magic = "magic";

    public const string Mixed = "mixed";

    /// <summary>
    /// A measured entry from a resolved profile, or null when the profile measured no damage
    /// at all — shares that cannot sum to 1 are not shares.
    /// </summary>
    public static ChampionDamageProfileReadModel? Measured(
        ChampionProfileFacts facts,
        string? position,
        IReadOnlyList<ChampionDamageProfileStat> buildRows)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(buildRows);

        if (facts.DamagePerGame <= 0)
        {
            return null;
        }

        var builds = Builds(buildRows);
        return new ChampionDamageProfileReadModel
        {
            ChampionId = facts.ChampionId,
            Position = position,
            ProfilePosition = facts.Position,
            Source = ChampionDamageProfileSources.Measured,
            Patch = facts.Patch,
            Games = facts.Games,
            PhysicalShare = facts.PhysicalShare,
            MagicShare = facts.MagicShare,
            TrueShare = Remainder(facts.PhysicalShare, facts.MagicShare),
            DamagePerGame = facts.DamagePerGame,
            Builds = builds,
            FlexDamage = IsFlex(builds),
        };
    }

    /// <summary>
    /// The builds of one <c>(champion, position, patch)</c> that clear the profile floor, most
    /// played first. The share is over the archetype table's own games, so a patch the table
    /// only partly covers (it fills forward from its deploy) is a smaller sample, not a skewed one.
    /// </summary>
    public static IReadOnlyList<ChampionDamageBuildReadModel> Builds(IReadOnlyList<ChampionDamageProfileStat> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var total = rows.Sum(row => row.Games);
        if (total <= 0)
        {
            return [];
        }

        return [.. rows
            .Where(row => row.Games >= ChampionProfileSnapshotRules.MinGames)
            .Select(row => (Row: row, Damage: (double)(row.PhysicalDamageToChampionsSum
                + row.MagicDamageToChampionsSum
                + row.TrueDamageToChampionsSum)))
            .Where(entry => entry.Damage > 0)
            .OrderByDescending(entry => entry.Row.Games)
            .ThenBy(entry => entry.Row.Archetype)
            .Select(entry =>
            {
                var physical = entry.Row.PhysicalDamageToChampionsSum / entry.Damage;
                var magic = entry.Row.MagicDamageToChampionsSum / entry.Damage;
                return new ChampionDamageBuildReadModel
                {
                    Archetype = entry.Row.Archetype.ToString(),
                    Games = entry.Row.Games,
                    Share = entry.Row.Games / (double)total,
                    PhysicalShare = physical,
                    MagicShare = magic,
                    TrueShare = Remainder(physical, magic),
                };
            })];
    }

    /// <summary>
    /// Whether two builds holding at least <see cref="FlexMinShare"/> each deal mostly
    /// different damage types — an AD and an AP Kai'Sa, not a crit and an on-hit Varus.
    /// </summary>
    public static bool IsFlex(IReadOnlyList<ChampionDamageBuildReadModel> builds)
    {
        ArgumentNullException.ThrowIfNull(builds);

        var dominant = builds
            .Where(build => build.Share >= FlexMinShare)
            .Select(build => build.MagicShare > build.PhysicalShare)
            .Distinct()
            .Count();
        return dominant > 1;
    }

    /// <summary>
    /// The static class of a champion with no measured profile, or null when Data Dragon does
    /// not rate it — then the champion is left out rather than guessed.
    /// </summary>
    public static string? FallbackClass(ChampionStatics statics)
    {
        ArgumentNullException.ThrowIfNull(statics);

        if (statics.AttackRating is not { } attack || statics.MagicRating is not { } magic)
        {
            return null;
        }

        return (magic - attack) switch
        {
            >= FallbackRatingGap => Magic,
            <= -FallbackRatingGap => Physical,
            _ => Mixed,
        };
    }

    /// <summary>The true share as what physical and magic leave, clamped against rounding.</summary>
    private static double Remainder(double physical, double magic) => Math.Max(0d, 1d - physical - magic);
}
