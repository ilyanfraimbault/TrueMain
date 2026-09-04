using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// The two additive <c>ON CONFLICT ... + EXCLUDED</c> upserts behind the item-context fold
/// (#1450): the per-item numerators and the per-slot denominators, written in the caller's
/// transaction so a batch can never leave one without the other.
/// </summary>
public static class ItemContextUpsert
{
    private const string StatsSql = """
        INSERT INTO champion_item_context_stats
            ("Id", "ChampionId", "Position", "Patch", "Slot", "ItemId", "Axis", "Bucket",
             "Games", "Wins", "AggregatedAtUtc")
        SELECT gen_random_uuid(), t.champ, t.position, t.patch, t.slot, t.item, t.axis, t.bucket,
               t.games, t.wins, @aggAt
        FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @slots::text[],
                    @items::integer[], @axes::text[], @buckets::text[],
                    @games::integer[], @wins::integer[])
            AS t(champ, position, patch, slot, item, axis, bucket, games, wins)
        ON CONFLICT ("Patch", "ChampionId", "Position", "Slot", "ItemId", "Axis", "Bucket") DO UPDATE SET
            "Games" = champion_item_context_stats."Games" + EXCLUDED."Games",
            "Wins" = champion_item_context_stats."Wins" + EXCLUDED."Wins",
            "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
        """;

    private const string TotalsSql = """
        INSERT INTO champion_item_context_totals
            ("Id", "ChampionId", "Position", "Patch", "Slot", "Axis", "Bucket",
             "Games", "Wins", "AggregatedAtUtc")
        SELECT gen_random_uuid(), t.champ, t.position, t.patch, t.slot, t.axis, t.bucket,
               t.games, t.wins, @aggAt
        FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @slots::text[],
                    @axes::text[], @buckets::text[], @games::integer[], @wins::integer[])
            AS t(champ, position, patch, slot, axis, bucket, games, wins)
        ON CONFLICT ("Patch", "ChampionId", "Position", "Slot", "Axis", "Bucket") DO UPDATE SET
            "Games" = champion_item_context_totals."Games" + EXCLUDED."Games",
            "Wins" = champion_item_context_totals."Wins" + EXCLUDED."Wins",
            "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
        """;

    public static async Task WriteAsync(
        TrueMainDbContext db,
        ItemContextAccumulator accumulator,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(accumulator);

        if (accumulator.Stats.Count > 0)
        {
            var rows = accumulator.Stats.ToList();
            await db.Database.ExecuteSqlRawAsync(
                StatsSql,
                [
                    new NpgsqlParameter("aggAt", aggregatedAtUtc),
                    new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                    new NpgsqlParameter("positions", rows.Select(r => r.Key.Position).ToArray()),
                    new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                    new NpgsqlParameter("slots", rows.Select(r => r.Key.Slot.ToString()).ToArray()),
                    new NpgsqlParameter("items", rows.Select(r => r.Key.ItemId).ToArray()),
                    new NpgsqlParameter("axes", rows.Select(r => r.Key.Axis.ToString()).ToArray()),
                    new NpgsqlParameter("buckets", rows.Select(r => r.Key.Bucket.ToString()).ToArray()),
                    new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                    new NpgsqlParameter("wins", rows.Select(r => r.Value.Wins).ToArray()),
                ],
                ct);
        }

        if (accumulator.Totals.Count > 0)
        {
            var rows = accumulator.Totals.ToList();
            await db.Database.ExecuteSqlRawAsync(
                TotalsSql,
                [
                    new NpgsqlParameter("aggAt", aggregatedAtUtc),
                    new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                    new NpgsqlParameter("positions", rows.Select(r => r.Key.Position).ToArray()),
                    new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                    new NpgsqlParameter("slots", rows.Select(r => r.Key.Slot.ToString()).ToArray()),
                    new NpgsqlParameter("axes", rows.Select(r => r.Key.Axis.ToString()).ToArray()),
                    new NpgsqlParameter("buckets", rows.Select(r => r.Key.Bucket.ToString()).ToArray()),
                    new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                    new NpgsqlParameter("wins", rows.Select(r => r.Value.Wins).ToArray()),
                ],
                ct);
        }
    }
}
