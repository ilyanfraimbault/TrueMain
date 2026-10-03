using System.Globalization;
using System.Text.Json;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes.Components.ItemContextAggregation;
using Microsoft.Extensions.Logging.Abstractions;
using NextItemEvaluation;

// Offline evaluation of the next-item model (#1749). Trains the model on the item-context
// counters of the patches *before* the test patch, then replays the test patch's real
// builds edge by edge and asks: did the model put the item the main actually completed
// next at the top (top-1), in its first three (top-3), and how much probability did it give
// it (log-loss)? The baseline is the same branch scored with no situation at all — "the
// branch's most common child", which is what the champion page's build tree already says.
//
// Input: a directory of psql CSV exports, produced by export.sql next to this file.
// Usage:  dotnet run --project backend/Tools/NextItemEvaluation -- <dir> <testPatch> <trainPatches,newest,first>
var directory = args.Length > 0 ? args[0] : ".";
var testPatch = args.Length > 1 ? args[1] : "16.19";
var trainWindow = args.Length > 2 ? args[2].Split(',') : ["16.18", "16.17", "16.16"];
var priors = args.Length > 3 ? args[3].Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray() : [10d, 25d, 50d, 100d, 200d];

string File(string name) => Path.Combine(directory, name);

var scopes = CsvRows.Records(File("scopes.csv")).Skip(1)
    .Select(record => (ChampionId: int.Parse(record[0], CultureInfo.InvariantCulture), Position: record[1]))
    .ToList();
var stats = CsvRows.Read<ChampionItemContextStat>(File("stats.csv"))
    .Where(row => trainWindow.Contains(row.Patch))
    .ToLookup(row => (row.ChampionId, row.Position));
var totals = CsvRows.Read<ChampionItemContextTotal>(File("totals.csv"))
    .Where(row => trainWindow.Contains(row.Patch))
    .ToLookup(row => (row.ChampionId, row.Position));
var profiles = ChampionProfileSnapshot.FromRows(
    CsvRows.Read<ChampionProfileStat>(File("profiles.csv")), testPatch, lookbackPatches: 2, minGames: 100);

var roster = CsvRows.Records(File("roster.csv")).Skip(1)
    .Select(record => new Seat(
        record[0],
        int.Parse(record[1], CultureInfo.InvariantCulture),
        int.Parse(record[2], CultureInfo.InvariantCulture),
        int.Parse(record[3], CultureInfo.InvariantCulture),
        record[4]))
    .ToLookup(seat => seat.MatchId);
var leads = CsvRows.Records(File("leads.csv")).Skip(1)
    .ToDictionary(
        record => (record[0], int.Parse(record[1], CultureInfo.InvariantCulture)),
        record => int.Parse(record[2], CultureInfo.InvariantCulture));

var testRecords = CsvRows.Records(File("tp.csv")).Skip(1).ToList();
using var http = new HttpClient();
var metadataProvider = new CommunityDragonItemMetadataProvider(
    http, NullLogger<CommunityDragonItemMetadataProvider>.Instance, TimeProvider.System);
var metadata = await metadataProvider.GetItemsAsync(testRecords[0][13], CancellationToken.None);
var thresholds = new DraftAxisThresholds();

// The test decisions, resolved once: every build edge and the boots of every test game,
// with the situations the game sat in — exactly as the fold would have counted them.
var decisions = new List<Decision>();
foreach (var record in testRecords)
{
    var matchId = record[0];
    var self = new Seat(matchId, int.Parse(record[1], CultureInfo.InvariantCulture), int.Parse(record[2], CultureInfo.InvariantCulture), int.Parse(record[4], CultureInfo.InvariantCulture), record[3]);
    var items = Enumerable.Range(6, 7).Select(i => int.TryParse(record[i], CultureInfo.InvariantCulture, out var id) ? id : 0).ToArray();
    var events = JsonSerializer.Deserialize<List<ItemEvent>>(record[14]) ?? [];
    var seats = roster[matchId].ToList();
    var opponent = seats.FirstOrDefault(other => other.Position == self.Position && other.TeamId != self.TeamId);

    double? lead = opponent is not null
        && leads.TryGetValue((matchId, self.ParticipantId), out var mine)
        && leads.TryGetValue((matchId, opponent.ParticipantId), out var theirs)
            ? mine - theirs
            : null;

    var situation = DraftAxisEvaluator.Evaluate(
        new DraftContext(
            Side(seats.Where(other => other.TeamId != self.TeamId)),
            Side(seats.Where(other => other.TeamId == self.TeamId && other.ParticipantId != self.ParticipantId)),
            opponent is null ? null : profiles.Find(opponent.ChampionId, opponent.Position),
            lead),
        thresholds);

    var finalItems = FinalInventory.Of(items[0], items[1], items[2], items[3], items[4], items[5], items[6]);
    var edges = ItemContextSlotResolver.Resolve(events, finalItems, metadata);
    var scope = (self.ChampionId, self.Position);

    if (edges.TryGetValue(ItemContextSlot.Build, out var build))
    {
        var owned = new HashSet<int>();
        foreach (var edge in build)
        {
            decisions.Add(new Decision(scope, ItemContextSlot.Build, edge.ParentItemId, edge.ItemId, [.. owned], situation));
            owned.Add(edge.ItemId);
        }
    }

    if (edges.TryGetValue(ItemContextSlot.Boots, out var boots))
    {
        decisions.Add(new Decision(scope, ItemContextSlot.Boots, 0, boots[0].ItemId, [], situation));
    }
}

Console.WriteLine($"test patch {testPatch}, trained on {string.Join('+', trainWindow)}, {scopes.Count} scopes, {testRecords.Count} games, {decisions.Count} decisions");
Console.WriteLine();

var draftOnly = (IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> situation)
    => (IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>)situation
        .Where(entry => ItemContextAxes.IsDraftTime(entry.Key))
        .ToDictionary(entry => entry.Key, entry => entry.Value);
var none = new Dictionary<ItemContextAxis, ItemContextBucket>();

foreach (var prior in priors)
{
    var options = new NextItemModelOptions { PriorGames = prior };
    var models = scopes.ToDictionary(
        scope => scope,
        scope => NextItemModel.From(NextItemTermBuilder.Build(
            new ItemContextScope(scope.ChampionId, scope.Position, trainWindow[0]),
            [.. stats[scope]],
            [.. totals[scope]],
            trainWindow,
            options,
            DateTime.UtcNow)));

    Console.WriteLine($"== prior {prior} pseudo-games ==");
    foreach (var slot in new[] { ItemContextSlot.Build, ItemContextSlot.Boots })
    {
        var baseline = new Tally();
        var model = new Tally();
        var draft = new Tally();
        var whereBaselineWrong = new Tally();
        var whereBaselineRight = new Tally();
        int overrides = 0, modelRight = 0, baselineRight = 0;

        foreach (var decision in decisions.Where(d => d.Slot == slot))
        {
            var branch = models[decision.Scope].Branch(slot, decision.ParentItemId);
            if (branch is null)
            {
                baseline.Uncovered++;
                continue;
            }

            var baselineScores = NextItemScorer.Score(branch, none, decision.Owned);
            var b = baseline.Add(baselineScores, decision.ItemId);
            var modelScores = NextItemScorer.Score(branch, decision.Situation, decision.Owned);
            model.Add(modelScores, decision.ItemId);
            draft.Add(NextItemScorer.Score(branch, draftOnly(decision.Situation), decision.Owned), decision.ItemId);

            (b ? whereBaselineRight : whereBaselineWrong).Add(modelScores, decision.ItemId);

            // The decisions the model actually changes: who is right when the two disagree?
            if (modelScores.Count > 0 && modelScores[0].ItemId != baselineScores[0].ItemId)
            {
                overrides++;
                modelRight += modelScores[0].ItemId == decision.ItemId ? 1 : 0;
                baselineRight += b ? 1 : 0;
            }
        }

        Console.WriteLine($"  {slot}: {baseline.Count} decisions scored, {baseline.Uncovered} on a branch the training patches never saw");
        Console.WriteLine($"    baseline (most common child)  {baseline}");
        Console.WriteLine($"    model                         {model}");
        Console.WriteLine($"    model, draft-time axes only   {draft}");
        Console.WriteLine($"    model where baseline top-1 wrong ({whereBaselineWrong.Count}): top-1 {whereBaselineWrong.Top1Rate:P1}");
        Console.WriteLine($"    model where baseline top-1 right ({whereBaselineRight.Count}): top-1 {whereBaselineRight.Top1Rate:P1}");
        Console.WriteLine($"    top-1 overridden by the model in {overrides} decisions: model right {modelRight}, baseline right {baselineRight}");
    }

    Console.WriteLine();
}

DraftSide Side(IEnumerable<Seat> members)
{
    var facts = new List<ChampionProfileFacts>();
    var missing = 0;
    foreach (var member in members)
    {
        if (profiles.Find(member.ChampionId, member.Position) is { } resolved)
        {
            facts.Add(resolved);
        }
        else
        {
            missing++;
        }
    }

    return new DraftSide(facts, missing);
}

internal sealed record Seat(string MatchId, int ParticipantId, int ChampionId, int TeamId, string Position);

internal sealed record Decision(
    (int ChampionId, string Position) Scope,
    ItemContextSlot Slot,
    int ParentItemId,
    int ItemId,
    HashSet<int> Owned,
    IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Situation);

internal sealed class Tally
{
    /// <summary>Probability floor for an item the model offered no share to, so one miss cannot make the log-loss infinite.</summary>
    private const double Floor = 1e-4;

    public int Count { get; private set; }

    public int Uncovered { get; set; }

    private int _top1;
    private int _top3;
    private double _logLoss;

    public double Top1Rate => Count == 0 ? 0 : _top1 / (double)Count;

    /// <summary>Scores one decision; returns whether the top candidate was the item actually built.</summary>
    public bool Add(IReadOnlyList<NextItemScore> scores, int actual)
    {
        Count++;
        var rank = scores.ToList().FindIndex(score => score.ItemId == actual);
        var hit = rank == 0;
        if (hit)
        {
            _top1++;
        }

        if (rank is >= 0 and < 3)
        {
            _top3++;
        }

        var share = rank >= 0 ? scores[rank].Share : 0d;
        _logLoss -= Math.Log(Math.Max(share, Floor));
        return hit;
    }

    public override string ToString()
        => Count == 0
            ? "no decisions"
            : $"top-1 {_top1 / (double)Count:P1}  top-3 {_top3 / (double)Count:P1}  log-loss {_logLoss / Count:F3}";
}
