using Core.Lol.Draft;
using Core.Lol.Patches;
using Data.Entities;
using Data.ItemContext;
using NextItemEvaluation;

namespace DraftEvaluation;

/// <summary>
/// The item-context fold's root build totals on the ally axes, and the profiles the allies
/// are placed with — the API's arithmetic (<c>DraftTeamFitReader</c>): the four allies placed
/// by <see cref="DraftAxisEvaluator"/>, then the pick's win rate in that band minus its win
/// rate over the same games unconditioned.
/// </summary>
internal sealed class TeamFitData
{
    private const int AlliesInTeam = 4;

    private static readonly DraftAxisThresholds Thresholds = new();

    private readonly Dictionary<(int, string, ItemContextAxis, ItemContextBucket), (int Games, int Wins)> _totals = [];
    private ChampionProfileSnapshot _profiles = ChampionProfileSnapshot.Empty;

    public static TeamFitData Load(string totals, string profiles)
    {
        var data = new TeamFitData();
        foreach (var r in Csv.Rows(totals))
        {
            data._totals[(Csv.Int(r[0]), r[1], Enum.Parse<ItemContextAxis>(r[2]), Enum.Parse<ItemContextBucket>(r[3]))] =
                (Csv.Int(r[4]), Csv.Int(r[5]));
        }

        // The newest training patch, reaching back like the API's snapshot does.
        var rows = CsvRows.Read<ChampionProfileStat>(profiles);
        var newest = rows.Select(row => row.Patch).Distinct()
            .Where(patch => PatchVersion.TryParse(patch, out _))
            .OrderByDescending(patch => PatchVersion.TryParse(patch, out var version) ? version : default)
            .FirstOrDefault();
        if (newest is not null)
        {
            data._profiles = ChampionProfileSnapshot.FromRows(
                rows, newest, ChampionProfileSnapshotRules.LookbackPatches, ChampionProfileSnapshotRules.MinGames);
        }

        return data;
    }

    /// <summary>The pick's reading on each ally axis its four allies can be placed on.</summary>
    public IReadOnlyList<DraftComponent> Axes(Sample sample)
    {
        var facts = sample.Allies
            .Select(ally => _profiles.Find(ally.ChampionId, ally.Position))
            .OfType<ChampionProfileFacts>()
            .ToList();
        var bands = DraftAxisEvaluator.Evaluate(
            new DraftContext(new DraftSide([], 0), new DraftSide(facts, AlliesInTeam - facts.Count), null, null),
            Thresholds);
        var overall = _totals.GetValueOrDefault(
            (sample.ChampionId, sample.Position, ItemContextAxis.Overall, ItemContextBucket.All));

        return [.. new[] { ItemContextAxis.AllyMagicDamage, ItemContextAxis.AllyFrontline }
            .Where(bands.ContainsKey)
            .Select(axis =>
            {
                var band = _totals.GetValueOrDefault((sample.ChampionId, sample.Position, axis, bands[axis]));
                return DraftScoring.Fit(band.Games, band.Wins, overall.Games, overall.Wins);
            })];
    }
}
