using Data;
using Data.BuildFacts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>The grain of a <c>champion_item_component_orders</c> row.</summary>
public readonly record struct ComponentOrderKey(
    int ChampionId,
    string Position,
    string Patch,
    int ItemId,
    int ComponentId,
    int Occurrence,
    int Rank);

/// <summary>
/// Accumulates one batch of games into the component-order counters (#2183) and writes them
/// with the same additive <c>ON CONFLICT ... + EXCLUDED</c> upsert as the item-context counters,
/// in the batch's transaction, so a match is never flagged with only half of its fold written.
/// </summary>
public sealed class ComponentOrderAccumulator
{
    private const string Sql = """
        INSERT INTO champion_item_component_orders
            ("Id", "ChampionId", "Position", "Patch", "ItemId", "ComponentId", "Occurrence", "Rank",
             "Games", "AggregatedAtUtc")
        SELECT gen_random_uuid(), t.champ, t.position, t.patch, t.item, t.component, t.occurrence, t.rank,
               t.games, @aggAt
        FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @items::integer[],
                    @components::integer[], @occurrences::integer[], @ranks::integer[], @games::integer[])
            AS t(champ, position, patch, item, component, occurrence, rank, games)
        ON CONFLICT ("ChampionId", "Position", "Patch", "ItemId", "ComponentId", "Occurrence", "Rank") DO UPDATE SET
            "Games" = champion_item_component_orders."Games" + EXCLUDED."Games",
            "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
        """;

    private readonly Dictionary<ComponentOrderKey, int> _games = [];

    public IReadOnlyDictionary<ComponentOrderKey, int> Games => _games;

    /// <summary>Folds one participant's component orders (<see cref="ComponentOrderResolver"/>).</summary>
    public void Add(ItemContextScope scope, IReadOnlyList<ComponentOrder> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        foreach (var order in orders)
        {
            foreach (var rank in order.Ranks)
            {
                var key = new ComponentOrderKey(
                    scope.ChampionId, scope.Position, scope.Patch, order.ItemId, rank.ComponentId, rank.Occurrence, rank.Rank);
                _games[key] = _games.GetValueOrDefault(key) + 1;
            }
        }
    }

    public async Task WriteAsync(TrueMainDbContext db, DateTime aggregatedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (_games.Count == 0)
        {
            return;
        }

        var rows = _games.ToList();
        await db.Database.ExecuteSqlRawAsync(
            Sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.Position).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("items", rows.Select(r => r.Key.ItemId).ToArray()),
                new NpgsqlParameter("components", rows.Select(r => r.Key.ComponentId).ToArray()),
                new NpgsqlParameter("occurrences", rows.Select(r => r.Key.Occurrence).ToArray()),
                new NpgsqlParameter("ranks", rows.Select(r => r.Key.Rank).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value).ToArray()),
            ],
            ct);
    }
}
