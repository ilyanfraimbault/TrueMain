using System.Globalization;
using Core.Lol.Draft;
using Core.Lol.Synergy;

// Offline back-test of the draft suggestions (#1906). Reads the matchup and synergy
// aggregates of the training patches, then replays the test patch's games: for every
// main's pick, is a higher score a better predictor of the game being won? The figure is
// the AUC of the score against the result — the probability that a won pick was scored
// above a lost one (0.5 = no signal). "A higher-ranked pick wins more often than a lower
// one", measured, for the old scoring (#1675: raw matchup + summed synergy) and the new one
// (#1906: shrunk lane term + mean synergy), over a grid of shrinkage k and synergy weight.
//
// Two moments of the draft are replayed:
//   - lane known: the whole board is visible (the real lane opponent, the four allies);
//   - blind: nothing is visible — the old scoring had no signal here (pool order), the new
//     one ranks by blind-pick safety; the champion's lane win rate is the reference.
//
// Input: a directory of psql CSV exports, produced by export.sql next to this file.
// Usage:  dotnet run --project backend/Tools/DraftEvaluation -- <dir>
var directory = args.Length > 0 ? args[0] : ".";
string File(string name) => Path.Combine(directory, name);

var data = EvaluationData.Load(File("matchups.csv"), File("baselines.csv"), File("synergy.csv"));
var games = Roster.Load(File("roster.csv"));
var samples = games.SelectMany(game => game.Samples()).ToList();
Console.WriteLine($"Test picks (mains): {samples.Count:N0} over {games.Count:N0} games");

// The pairing floors of the synergy service, for the old scoring (ChampionsListOptions defaults).
const int OldMinSynergyGames = 20;
const double OldMinSynergyPlayRate = 0.01;

double OldScore(Sample sample)
{
    var record = data.Record(sample.ChampionId, sample.Position);
    var matchup = sample.OpponentChampionId is { } opponent
        ? record.GetValueOrDefault(opponent, DraftComponent.None).Delta
        : 0d;
    var synergy = sample.Allies.Sum(ally =>
    {
        var pairing = data.Synergy(sample.ChampionId, sample.Position, ally.ChampionId, ally.Position);
        var floor = Math.Max(OldMinSynergyGames, (int)Math.Ceiling(OldMinSynergyPlayRate * data.SelfGames(sample.ChampionId, sample.Position)));
        return pairing.Games >= floor ? pairing.Delta : 0d;
    });
    return matchup + synergy;
}

double NewLaneKnownScore(Sample sample, DraftScoringWeights weights)
{
    var record = data.Record(sample.ChampionId, sample.Position);
    var occupancy = sample.OpponentChampionId is { } opponent
        ? new Dictionary<int, double> { [opponent] = 1d }
        : new Dictionary<int, double>();
    var (_, laneShrunk, occupied) = DraftScoring.Lane(record, occupancy, weights.ShrinkGames);
    var allies = sample.Allies
        .Select(ally => new DraftAllyPairing(
            data.Synergy(sample.ChampionId, sample.Position, ally.ChampionId, ally.Position), Hovered: false))
        .ToList();
    var (_, synergyShrunk) = DraftScoring.Synergy(allies, weights);
    return DraftScoring.Score(laneShrunk, occupied, 0d, synergyShrunk, weights);
}

double NewBlindScore(Sample sample, double k)
{
    var shares = data.LaneShares(sample.Position)
        .Where(entry => entry.Key != sample.ChampionId)
        .ToDictionary(entry => entry.Key, entry => entry.Value);
    return DraftScoring.Blind(data.Record(sample.ChampionId, sample.Position), shares, k).Shrunk;
}

var known = samples.Where(sample => sample.OpponentChampionId is not null).ToList();
Console.WriteLine();
Console.WriteLine("Lane known — AUC of the score against the result");
Console.WriteLine($"  old (raw matchup + summed synergy) : {Auc.Of(known, OldScore):F4}");
foreach (var k in new[] { 0d, 25d, 50d, 100d, 200d, 400d })
{
    var row = new[] { 0d, 0.25d, 0.5d, 1d }
        .Select(synergy => Auc.Of(known, sample => NewLaneKnownScore(
            sample, DraftScoringWeights.Default with { ShrinkGames = k, Synergy = synergy })))
        .Select(auc => auc.ToString("F4", CultureInfo.InvariantCulture));
    Console.WriteLine($"  new k={k,4} synergy 0 / .25 / .5 / 1 : {string.Join("  ", row)}");
}

Console.WriteLine();
Console.WriteLine("Blind — AUC of the score against the result");
Console.WriteLine($"  lane win rate (reference)          : {Auc.Of(samples, sample => data.LaneWinRate(sample.ChampionId, sample.Position)):F4}");
foreach (var k in new[] { 0d, 25d, 50d, 100d, 200d, 400d })
{
    Console.WriteLine($"  blind safety k={k,4}                : {Auc.Of(samples, sample => NewBlindScore(sample, k)):F4}");
}

/// <summary>The training aggregates, folded to what the scoring reads.</summary>
internal sealed class EvaluationData
{
    /// <summary>The synergy service's baseline floor (<c>ChampionsList:MinSynergyBaselineGames</c>).</summary>
    private const int MinBaselineGames = 50;

    private readonly Dictionary<(int, string), Dictionary<int, DraftComponent>> _records = [];
    private readonly Dictionary<(int, string), (int Games, int Wins)> _lane = [];
    private readonly Dictionary<(int, string), (int Games, int Wins)> _self = [];
    private readonly Dictionary<(int, string), (int Games, int Wins)> _ally = [];
    private readonly Dictionary<(int, string, int, string), (int Games, int Wins)> _pairs = [];
    private readonly Dictionary<string, Dictionary<int, double>> _laneShares = [];
    private double _cohortRate;

    public static EvaluationData Load(string matchups, string baselines, string synergy)
    {
        var data = new EvaluationData();

        var rows = Csv.Rows(matchups)
            .Select(r => (Champion: Csv.Int(r[0]), Position: r[1], Opponent: Csv.Int(r[2]), Games: Csv.Int(r[3]), Wins: Csv.Int(r[4])))
            .ToList();
        foreach (var group in rows.GroupBy(r => (r.Champion, r.Position)))
        {
            var games = group.Sum(r => r.Games);
            var wins = group.Sum(r => r.Wins);
            data._lane[group.Key] = (games, wins);
            var overall = games == 0 ? 0d : (double)wins / games;
            data._records[group.Key] = group
                .Where(r => r.Games > 0)
                .ToDictionary(r => r.Opponent, r => new DraftComponent(((double)r.Wins / r.Games) - overall, r.Games));
        }

        long cohortGames = 0, cohortWins = 0;
        foreach (var r in Csv.Rows(baselines))
        {
            var key = (Csv.Int(r[1]), r[2]);
            var value = (Csv.Int(r[3]), Csv.Int(r[4]));
            if (r[0] == "SELF")
            {
                data._self[key] = value;
                cohortGames += value.Item1;
                cohortWins += value.Item2;
            }
            else if (r[0] == "ALLY")
            {
                data._ally[key] = value;
                if (!data._laneShares.TryGetValue(r[2], out var shares))
                {
                    data._laneShares[r[2]] = shares = [];
                }

                shares[key.Item1] = value.Item1;
            }
        }

        data._cohortRate = cohortGames == 0 ? 0.5 : (double)cohortWins / cohortGames;

        foreach (var r in Csv.Rows(synergy))
        {
            data._pairs[(Csv.Int(r[0]), r[1], Csv.Int(r[2]), r[3])] = (Csv.Int(r[4]), Csv.Int(r[5]));
        }

        return data;
    }

    public IReadOnlyDictionary<int, DraftComponent> Record(int championId, string position)
        => _records.GetValueOrDefault((championId, position)) ?? [];

    public IReadOnlyDictionary<int, double> LaneShares(string position)
        => _laneShares.GetValueOrDefault(position) ?? [];

    public double LaneWinRate(int championId, string position)
        => _lane.TryGetValue((championId, position), out var lane) && lane.Games > 0
            ? (double)lane.Wins / lane.Games
            : 0.5;

    public int SelfGames(int championId, string position) => _self.GetValueOrDefault((championId, position)).Games;

    /// <summary>
    /// Observed minus expected, with the expectation built exactly as the synergy service
    /// does (<c>SynergyMath</c> over the SELF / ALLY baselines and the cohort rate).
    /// </summary>
    public DraftComponent Synergy(int championId, string position, int partnerId, string partnerPosition)
    {
        if (!_pairs.TryGetValue((championId, position, partnerId, partnerPosition), out var pair) || pair.Games == 0)
        {
            return DraftComponent.None;
        }

        var self = _self.GetValueOrDefault((championId, position));
        var ally = _ally.GetValueOrDefault((partnerId, partnerPosition));
        if (self.Games < MinBaselineGames || ally.Games < MinBaselineGames)
        {
            return DraftComponent.None;
        }

        var expected = SynergyMath.ExpectedWinRate(
            (double)self.Wins / self.Games, [(double)ally.Wins / ally.Games], _cohortRate);
        return new DraftComponent(((double)pair.Wins / pair.Games) - expected, pair.Games);
    }
}

/// <summary>One main's pick in a test game, with what the board showed.</summary>
internal sealed record Sample(
    int ChampionId,
    string Position,
    bool Win,
    int? OpponentChampionId,
    IReadOnlyList<(int ChampionId, string Position)> Allies);

internal sealed record Seat(int ChampionId, int TeamId, string Position, bool Win, bool IsMain);

internal sealed record Roster(IReadOnlyList<Seat> Seats)
{
    private static readonly HashSet<string> Lanes = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public static List<Roster> Load(string path)
        => Csv.Rows(path)
            .Select(r => (Match: r[0], Seat: new Seat(Csv.Int(r[1]), Csv.Int(r[2]), r[3], Csv.Bool(r[4]), Csv.Bool(r[5]))))
            .GroupBy(r => r.Match)
            .Select(group => new Roster(group.Select(r => r.Seat).ToList()))
            .ToList();

    public IEnumerable<Sample> Samples()
    {
        foreach (var seat in Seats.Where(seat => seat.IsMain && Lanes.Contains(seat.Position)))
        {
            var opponent = Seats.FirstOrDefault(other => other.TeamId != seat.TeamId && other.Position == seat.Position);
            var allies = Seats
                .Where(other => other.TeamId == seat.TeamId && other != seat && Lanes.Contains(other.Position))
                .Select(other => (other.ChampionId, other.Position))
                .ToList();
            yield return new Sample(seat.ChampionId, seat.Position, seat.Win, opponent?.ChampionId, allies);
        }
    }
}

internal static class Auc
{
    /// <summary>Mann–Whitney AUC: the share of (won, lost) pairs where the won pick scored higher, ties counted half.</summary>
    public static double Of(IReadOnlyList<Sample> samples, Func<Sample, double> score)
    {
        var scored = samples.Select(sample => (Score: score(sample), sample.Win)).OrderBy(entry => entry.Score).ToList();
        long positives = scored.Count(entry => entry.Win);
        long negatives = scored.Count - positives;
        if (positives == 0 || negatives == 0)
        {
            return 0.5;
        }

        double rankSum = 0d;
        var index = 0;
        while (index < scored.Count)
        {
            var end = index;
            while (end + 1 < scored.Count && scored[end + 1].Score.Equals(scored[index].Score))
            {
                end++;
            }

            var averageRank = ((index + end) / 2d) + 1d;
            rankSum += averageRank * scored.Skip(index).Take(end - index + 1).Count(entry => entry.Win);
            index = end + 1;
        }

        return (rankSum - (positives * (positives + 1) / 2d)) / (positives * (double)negatives);
    }
}

internal static class Csv
{
    /// <summary>The data rows of a header-first CSV of plain fields (ids, lanes, counts, booleans).</summary>
    public static IEnumerable<string[]> Rows(string path)
        => System.IO.File.ReadLines(path).Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));

    public static int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    public static bool Bool(string value) => value is "t" or "true" or "True";
}
