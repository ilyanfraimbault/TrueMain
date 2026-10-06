using Data;
using Data.BuildFacts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Processes.Components.ProfileAggregation;

/// <summary>
/// The grain of one <c>champion_damage_profile_stats</c> row (#1905).
/// </summary>
public readonly record struct DamageProfileKey(int ChampionId, string Position, string Patch, ItemArchetype Archetype);

/// <summary>The additive sums of one damage-profile row as the fold accumulates them.</summary>
public sealed class DamageProfileAccumulator
{
    public int Games;
    public long PhysicalDamageSum;
    public long MagicDamageSum;
    public long TrueDamageSum;
}

/// <summary>
/// The additive <c>ON CONFLICT ... + EXCLUDED</c> upsert of the per-archetype damage
/// sums, written in the same transaction as <see cref="ChampionProfileUpsert"/> so the
/// two tables are folded from exactly the same matches.
/// </summary>
public static class ChampionDamageProfileUpsert
{
    private const string UpsertSql = """
        INSERT INTO champion_damage_profile_stats
            ("Id", "ChampionId", "Position", "Patch", "Archetype", "Games",
             "PhysicalDamageToChampionsSum", "MagicDamageToChampionsSum", "TrueDamageToChampionsSum", "AggregatedAtUtc")
        SELECT gen_random_uuid(), t.champ, t.position, t.patch, t.archetype, t.games, t.physical, t.magic, t.true_damage, @aggAt
        FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @archetypes::text[],
                    @games::integer[], @physical::bigint[], @magic::bigint[], @trueDamage::bigint[])
            AS t(champ, position, patch, archetype, games, physical, magic, true_damage)
        ON CONFLICT ("Patch", "ChampionId", "Position", "Archetype") DO UPDATE SET
            "Games" = champion_damage_profile_stats."Games" + EXCLUDED."Games",
            "PhysicalDamageToChampionsSum" = champion_damage_profile_stats."PhysicalDamageToChampionsSum" + EXCLUDED."PhysicalDamageToChampionsSum",
            "MagicDamageToChampionsSum" = champion_damage_profile_stats."MagicDamageToChampionsSum" + EXCLUDED."MagicDamageToChampionsSum",
            "TrueDamageToChampionsSum" = champion_damage_profile_stats."TrueDamageToChampionsSum" + EXCLUDED."TrueDamageToChampionsSum",
            "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
        """;

    public static async Task WriteAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<DamageProfileKey, DamageProfileAccumulator> profiles,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (profiles.Count == 0)
        {
            return;
        }

        var rows = profiles.ToList();
        var parameters = new List<NpgsqlParameter>
        {
            new("aggAt", aggregatedAtUtc),
            new("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
            new("positions", rows.Select(r => r.Key.Position).ToArray()),
            new("patches", rows.Select(r => r.Key.Patch).ToArray()),
            // By name: the column is the enum's string conversion (see its configuration).
            new("archetypes", rows.Select(r => r.Key.Archetype.ToString()).ToArray()),
            new("games", rows.Select(r => r.Value.Games).ToArray()),
            new("physical", rows.Select(r => r.Value.PhysicalDamageSum).ToArray()),
            new("magic", rows.Select(r => r.Value.MagicDamageSum).ToArray()),
            new("trueDamage", rows.Select(r => r.Value.TrueDamageSum).ToArray()),
        };

        await db.Database.ExecuteSqlRawAsync(UpsertSql, parameters, ct);
    }
}
