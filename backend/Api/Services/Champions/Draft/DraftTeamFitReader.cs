using Core.Lol.Draft;
using Data;
using Data.ItemContext;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Draft;

/// <summary>An ally on the board, as the composition term places our team.</summary>
public sealed record DraftBoardAlly(int ChampionId, string Position, bool Hovered);

/// <summary>
/// One axis of our team's composition, the band the board puts it in, and the
/// candidate's win rate there against its win rate overall (#1906).
/// </summary>
public sealed record DraftTeamFitAxis(ItemContextAxis Axis, ItemContextBucket Bucket, DraftComponent Fit);

/// <summary>
/// The candidate's readings on every axis the board can place our team on.
/// </summary>
/// <param name="Axes">One per placed axis; a candidate with no games there carries <see cref="DraftComponent.None"/>.</param>
/// <param name="Tentative">True when an ally only hovering is part of the placement.</param>
/// <param name="Patch">The patch read, or the two-patch window it fell back to.</param>
public sealed record DraftTeamFit(IReadOnlyList<DraftTeamFitAxis> Axes, bool Tentative, string? Patch)
{
    public static readonly DraftTeamFit Empty = new([], false, null);
}

public interface IDraftTeamFitReader
{
    /// <summary>Each candidate's composition readings, for the allies on the board.</summary>
    Task<IReadOnlyDictionary<int, DraftTeamFit>> ReadAsync(
        IReadOnlyCollection<int> candidates,
        string position,
        IReadOnlyList<DraftBoardAlly> allies,
        DraftPatchScope scope,
        CancellationToken ct);
}

/// <summary>
/// The team-composition term of the draft (#1906): does the candidate win more, or
/// less, when its allies' damage is as magic-heavy — and its frontline as thick — as
/// the board shows?
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured on the item-context fold's own counters, no aggregate of its own.</b>
/// <c>champion_item_context_totals</c> already counts, per champion and lane, the games
/// and wins in each band of <see cref="ItemContextAxis.AllyMagicDamage"/> and
/// <see cref="ItemContextAxis.AllyFrontline"/>, beside the same games unconditioned
/// (<see cref="ItemContextAxis.Overall"/>). The root branch of the build slot is read:
/// every game that completed a first item, the widest denominator the fold keeps. Both
/// rates come from that one cohort, so what it leaves out (games over before a first
/// item) cancels in the difference.
/// </para>
/// <para>
/// <b>The board is placed exactly as the fold placed its games</b>: the same
/// <see cref="DraftAxisEvaluator"/>, the same profile snapshot rules and thresholds, the
/// same snapshot the next-item read caches. An ally slot nobody has picked yet counts as
/// an unprofiled member, so the axes appear once three of the four allies are on the
/// board — a team of one is not a composition.
/// </para>
/// <para>
/// <b>Every rank together</b>: the fold has no elo dimension (a band is far rarer than a
/// champion). The patch falls back like the lane records, per champion.
/// </para>
/// </remarks>
public sealed class DraftTeamFitReader(TrueMainDbContext db, IChampionReadCache cache) : IDraftTeamFitReader
{
    /// <summary>The four teammates the fold's ally axes are measured over.</summary>
    private const int AlliesInTeam = 4;

    private static readonly DraftAxisThresholds Thresholds = new();

    /// <summary>The axes of our own side the composition term reads.</summary>
    private static readonly ItemContextAxis[] TeamAxes = [ItemContextAxis.AllyMagicDamage, ItemContextAxis.AllyFrontline];

    public async Task<IReadOnlyDictionary<int, DraftTeamFit>> ReadAsync(
        IReadOnlyCollection<int> candidates,
        string position,
        IReadOnlyList<DraftBoardAlly> allies,
        DraftPatchScope scope,
        CancellationToken ct)
    {
        var board = allies.Where(ally => ally.Position != position).ToList();
        if (candidates.Count == 0 || scope.Current is not { } patch || board.Count < AlliesInTeam - 1)
        {
            return new Dictionary<int, DraftTeamFit>();
        }

        // The cache entry the next-item read fills: one snapshot per patch, whoever asks first.
        var profiles = await cache.GetOrComputeAsync(
            $"champions:next-item:profiles:{patch}",
            token => ChampionProfileSnapshotRules.LoadAsync(db, patch, token),
            ct,
            size: 8);

        var bands = Place(board, profiles);
        if (bands.Count == 0)
        {
            return new Dictionary<int, DraftTeamFit>();
        }

        var pool = candidates.Distinct().Order().ToList();
        var bandKey = string.Join(',', bands.Select(band => $"{band.Key}={band.Value}"));
        var tentative = board.Any(ally => ally.Hovered);
        var fits = await cache.GetOrComputeAsync(
            $"champions:draft:team-fit:{position}:{string.Join(',', pool)}:{scope.Token}:{bandKey}",
            token => ComputeAsync(pool, position, bands, scope, token),
            ct);

        return fits.ToDictionary(entry => entry.Key, entry => entry.Value with { Tentative = tentative });
    }

    /// <summary>
    /// Where the board puts our team on each axis the term reads, or nothing for an axis
    /// the evaluator cannot place (two allies missing or unprofiled).
    /// </summary>
    internal static IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Place(
        IReadOnlyList<DraftBoardAlly> board,
        ChampionProfileSnapshot profiles)
    {
        var facts = board
            .Select(ally => profiles.Find(ally.ChampionId, ally.Position))
            .OfType<ChampionProfileFacts>()
            .ToList();
        var allySide = new DraftSide(facts, AlliesInTeam - facts.Count);
        var axes = DraftAxisEvaluator.Evaluate(new DraftContext(new DraftSide([], 0), allySide, null, null), Thresholds);

        return TeamAxes
            .Where(axes.ContainsKey)
            .ToDictionary(axis => axis, axis => axes[axis]);
    }

    private async Task<IReadOnlyDictionary<int, DraftTeamFit>> ComputeAsync(
        IReadOnlyList<int> pool,
        string position,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> bands,
        DraftPatchScope scope,
        CancellationToken ct)
    {
        var window = scope.Window ?? [];
        // Both team axes and the unconditioned one, as constants: the band filter is applied
        // in memory, on a handful of rows per champion.
        var rows = await db.ChampionItemContextTotals
            .AsNoTracking()
            .Where(t => window.Contains(t.Patch)
                && pool.Contains(t.ChampionId)
                && t.Position == position
                && t.Slot == ItemContextSlot.Build
                && t.ParentItemId == 0
                && (t.Axis == ItemContextAxis.Overall
                    || t.Axis == ItemContextAxis.AllyMagicDamage
                    || t.Axis == ItemContextAxis.AllyFrontline))
            .Select(t => new TotalRow(t.ChampionId, t.Patch, t.Axis, t.Bucket, t.Games, t.Wins))
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.ChampionId)
            .ToDictionary(group => group.Key, group => Build(group.ToList(), bands, scope));
    }

    /// <summary>
    /// One champion's readings, on the current patch alone or on the window when the
    /// current patch holds fewer than <see cref="DraftLaneReader.MinCurrentPatchGames"/>
    /// of its games — the lane records' rule.
    /// </summary>
    internal static DraftTeamFit Build(
        IReadOnlyList<TotalRow> rows,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> bands,
        DraftPatchScope scope)
    {
        static (int Games, int Wins) Sum(IEnumerable<TotalRow> slice)
            => slice.Aggregate((Games: 0, Wins: 0), (sum, row) => (sum.Games + row.Games, sum.Wins + row.Wins));

        var current = rows.Where(row => row.Patch == scope.Current).ToList();
        var fallBack = scope.Previous is not null
            && Sum(current.Where(row => row.Axis == ItemContextAxis.Overall)).Games < DraftLaneReader.MinCurrentPatchGames;
        var used = fallBack ? rows : current;

        var overall = Sum(used.Where(row => row.Axis == ItemContextAxis.Overall));
        var axes = bands
            .Select(band =>
            {
                var inBand = Sum(used.Where(row => row.Axis == band.Key && row.Bucket == band.Value));
                return new DraftTeamFitAxis(
                    band.Key, band.Value, DraftScoring.Fit(inBand.Games, inBand.Wins, overall.Games, overall.Wins));
            })
            .ToList();

        return new DraftTeamFit(axes, false, fallBack ? $"{scope.Current}+{scope.Previous}" : scope.Current);
    }

    internal sealed record TotalRow(
        int ChampionId,
        string Patch,
        ItemContextAxis Axis,
        ItemContextBucket Bucket,
        int Games,
        int Wins);
}
