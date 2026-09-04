using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Processes.Components.ProfileAggregation;

/// <summary>
/// The grain of one <c>champion_profile_stats</c> row.
/// </summary>
public readonly record struct ProfileKey(int ChampionId, string Position, string Patch);

/// <summary>
/// The additive sums of one profile row as the fold accumulates them, in the same order
/// as the columns <see cref="ChampionProfileUpsert"/> writes.
/// </summary>
public sealed class ProfileAccumulator
{
    public int Games;
    public int Wins;
    public long GameDurationSecondsSum;
    public long PhysicalDamageSum;
    public long MagicDamageSum;
    public long TrueDamageSum;
    public long TotalHealSum;
    public long HealsOnTeammatesSum;
    public long DamageShieldedSum;
    public long TimeCCingOthersSum;
    public long TotalTimeCCDealtSum;
    public long DamageTakenSum;
    public long DamageSelfMitigatedSum;
    public int TeamDamageTakenGames;
    public long TeamDamageTakenSum;
    public int LaneGamesAt10;
    public long GoldLeadAt10Sum;
    public long XpLeadAt10Sum;
    public int KillsBy10Sum;
    public int LaneGamesAt15;
    public long GoldLeadAt15Sum;
    public long XpLeadAt15Sum;
    public int ItemGames;
    public int CritGames;
    public int ArmorPenetrationGames;
    public int OnHitGames;
    public int AbilityPowerGames;
    public int TankGames;
    public bool? IsRanged;
}

/// <summary>
/// The additive <c>ON CONFLICT ... + EXCLUDED</c> upsert behind
/// <c>ChampionProfileAggregationProcess</c> (#1449). The SQL is generated from one
/// column list so the INSERT column list, the unnest arrays and the update clauses
/// cannot drift from one another across thirty columns; the ranged flag is the one
/// non-additive column and is <c>COALESCE</c>d instead of summed.
/// </summary>
public static class ChampionProfileUpsert
{
    /// <summary>
    /// The additive columns in upsert order. The SQL is generated from this list so the
    /// INSERT column list, the unnest arrays and the <c>+ EXCLUDED</c> clauses cannot
    /// drift from one another across thirty columns.
    /// </summary>
    private static readonly (string Column, string PgType, Func<ProfileAccumulator, object> Value)[] SumColumns =
    [
        ("Games", "integer", a => a.Games),
        ("Wins", "integer", a => a.Wins),
        ("GameDurationSecondsSum", "bigint", a => a.GameDurationSecondsSum),
        ("PhysicalDamageToChampionsSum", "bigint", a => a.PhysicalDamageSum),
        ("MagicDamageToChampionsSum", "bigint", a => a.MagicDamageSum),
        ("TrueDamageToChampionsSum", "bigint", a => a.TrueDamageSum),
        ("TotalHealSum", "bigint", a => a.TotalHealSum),
        ("HealsOnTeammatesSum", "bigint", a => a.HealsOnTeammatesSum),
        ("DamageShieldedOnTeammatesSum", "bigint", a => a.DamageShieldedSum),
        ("TimeCCingOthersSum", "bigint", a => a.TimeCCingOthersSum),
        ("TotalTimeCCDealtSum", "bigint", a => a.TotalTimeCCDealtSum),
        ("DamageTakenSum", "bigint", a => a.DamageTakenSum),
        ("DamageSelfMitigatedSum", "bigint", a => a.DamageSelfMitigatedSum),
        ("TeamDamageTakenGames", "integer", a => a.TeamDamageTakenGames),
        ("TeamDamageTakenSum", "bigint", a => a.TeamDamageTakenSum),
        ("LaneGamesAt10", "integer", a => a.LaneGamesAt10),
        ("GoldLeadAt10Sum", "bigint", a => a.GoldLeadAt10Sum),
        ("XpLeadAt10Sum", "bigint", a => a.XpLeadAt10Sum),
        ("KillsBy10Sum", "integer", a => a.KillsBy10Sum),
        ("LaneGamesAt15", "integer", a => a.LaneGamesAt15),
        ("GoldLeadAt15Sum", "bigint", a => a.GoldLeadAt15Sum),
        ("XpLeadAt15Sum", "bigint", a => a.XpLeadAt15Sum),
        ("ItemGames", "integer", a => a.ItemGames),
        ("CritGames", "integer", a => a.CritGames),
        ("ArmorPenetrationGames", "integer", a => a.ArmorPenetrationGames),
        ("OnHitGames", "integer", a => a.OnHitGames),
        ("AbilityPowerGames", "integer", a => a.AbilityPowerGames),
        ("TankGames", "integer", a => a.TankGames),
    ];

    private static readonly string UpsertSql = BuildUpsertSql();

    private static string BuildUpsertSql()
    {
        var sumNames = string.Join(", ", SumColumns.Select(c => $"\"{c.Column}\""));
        var sumSelects = string.Join(", ", SumColumns.Select((c, i) => $"t.s{i}"));
        var sumUnnest = string.Join(", ", SumColumns.Select((c, i) => $"@s{i}::{c.PgType}[]"));
        var sumAliases = string.Join(", ", SumColumns.Select((c, i) => $"s{i}"));
        var sumUpdates = string.Join(",\n                ",
            SumColumns.Select(c => $"\"{c.Column}\" = champion_profile_stats.\"{c.Column}\" + EXCLUDED.\"{c.Column}\""));

        return $"""
            INSERT INTO champion_profile_stats
                ("Id", "ChampionId", "Position", "Patch", {sumNames}, "IsRanged", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.position, t.patch, {sumSelects}, t.ranged, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @patches::text[], {sumUnnest}, @ranged::boolean[])
                AS t(champ, position, patch, {sumAliases}, ranged)
            ON CONFLICT ("Patch", "ChampionId", "Position") DO UPDATE SET
                {sumUpdates},
                "IsRanged" = COALESCE(EXCLUDED."IsRanged", champion_profile_stats."IsRanged"),
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;
    }

    public static async Task WriteAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<ProfileKey, ProfileAccumulator> profiles,
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
            new("ranged", rows.Select(r => r.Value.IsRanged).ToArray()),
        };

        for (var i = 0; i < SumColumns.Length; i++)
        {
            var column = SumColumns[i];
            object array = column.PgType == "bigint"
                ? rows.Select(r => Convert.ToInt64(column.Value(r.Value))).ToArray()
                : rows.Select(r => Convert.ToInt32(column.Value(r.Value))).ToArray();
            parameters.Add(new NpgsqlParameter($"s{i}", array));
        }

        await db.Database.ExecuteSqlRawAsync(UpsertSql, parameters, ct);
    }
}
