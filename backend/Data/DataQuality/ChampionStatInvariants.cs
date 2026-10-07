using Core.Lol.Map;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Data.DataQuality;

/// <summary>
/// The <c>CHECK</c> constraints that pin what a champion stat row can hold (#1365): a win
/// is one of the games, a judged lane is one of the games, a lane is one of the five, and
/// an opponent or partner is a real champion.
///
/// <para>
/// <b>Why constraints and not a detector.</b> Every one of these tables is an additive
/// fold whose frozen patches can never be recomputed (#466), so a writer regression that
/// stores <c>Wins &gt; Games</c>, a blank lane or champion 0 is permanent the moment the
/// patch freezes, and no read can tell it from a real figure. The guard has to sit on the
/// write: the fold fails on the spot, on the batch that would have corrupted the row.
/// </para>
///
/// <para>
/// <b>One sentinel, pinned.</b> The lane columns of the folds are written from
/// <see cref="Aggregation.ChampionCohort"/> members only, so they carry a canonical lane
/// and nothing else. <c>champion_aggregate_scopes.Position</c> is the one column whose
/// "no lane" sentinel is part of the schema (<see cref="Aggregation.ChampionDirectoryLines"/>);
/// it is pinned to <c>''</c>, the single allowed spelling — the table had been written both
/// <c>''</c> and <c>' '</c>, which is why <c>CarriesLane</c> trims.
/// </para>
///
/// <para>
/// <b>Applied by convention for the counter pair.</b> Every mapped table carrying both
/// an <c>int Games</c> and an <c>int Wins</c> gets the <c>Wins &lt;= Games</c> check, so a
/// new stat table is covered by the next migration without anyone remembering to add it.
/// </para>
///
/// <para>
/// <b>Not validated against history.</b> The migration adds them <c>NOT VALID</c>: every
/// insert and update is checked from then on, but rows already written are not scanned.
/// Frozen patches hold rows folded under older rules (pre-#1087 positions, pre-#1445
/// lane counters folded off a separate flag) that no re-fold can rebuild, and a failed
/// <c>VALIDATE</c> would halt the deploy that carries it.
/// </para>
/// </summary>
public static class ChampionStatInvariants
{
    private const string WinsColumn = "Wins";
    private const string GamesColumn = "Games";

    /// <summary>The <c>IN (...)</c> list of the five canonical lanes, as SQL literals.</summary>
    private static readonly string CanonicalLaneList =
        string.Join(", ", LanePositions.All.Select(lane => $"'{lane}'"));

    /// <summary>Name of the <c>Wins &lt;= Games</c> check on <paramref name="table"/>.</summary>
    public static string WinsWithinGamesName(string table) => $"CK_{table}_WinsWithinGames";

    internal static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.GetTableName() is { } table && CarriesWinsAndGames(entityType))
            {
                entityType.AddCheckConstraint(
                    WinsWithinGamesName(table), $"\"{WinsColumn}\" <= \"{GamesColumn}\"");
            }
        }

        modelBuilder.Entity<ChampionMatchupStat>().ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_champion_matchup_stats_LaneGamesWithinGames", "\"LaneGames\" <= \"Games\"");
            table.HasCheckConstraint(
                "CK_champion_matchup_stats_CanonicalTeamPosition", CanonicalLane("TeamPosition"));
            table.HasCheckConstraint(
                "CK_champion_matchup_stats_OpponentChampionId", "\"OpponentChampionId\" > 0");
        });

        modelBuilder.Entity<ChampionOpponentStat>().ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_champion_opponent_stats_CanonicalTeamPosition", CanonicalLane("TeamPosition"));
            table.HasCheckConstraint(
                "CK_champion_opponent_stats_OpponentChampionId", "\"OpponentChampionId\" > 0");
        });

        modelBuilder.Entity<ChampionOpponentBaselineStat>().ToTable(table => table.HasCheckConstraint(
            "CK_champion_opponent_baseline_stats_CanonicalTeamPosition", CanonicalLane("TeamPosition")));

        modelBuilder.Entity<ChampionSynergyStat>().ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_champion_synergy_stats_CanonicalTeamPosition", CanonicalLane("TeamPosition"));
            table.HasCheckConstraint(
                "CK_champion_synergy_stats_CanonicalPartnerPosition", CanonicalLane("PartnerPosition"));
            table.HasCheckConstraint(
                "CK_champion_synergy_stats_PartnerChampionId", "\"PartnerChampionId\" > 0");
        });

        modelBuilder.Entity<ChampionSynergyBaselineStat>().ToTable(table => table.HasCheckConstraint(
            "CK_champion_synergy_baseline_stats_CanonicalTeamPosition", CanonicalLane("TeamPosition")));

        modelBuilder.Entity<ChampionProfileStat>().ToTable(table => table.HasCheckConstraint(
            "CK_champion_profile_stats_CanonicalPosition", CanonicalLane("Position")));

        modelBuilder.Entity<ChampionAggregateScope>().ToTable(table => table.HasCheckConstraint(
            "CK_champion_aggregate_scopes_PositionOrNoLane", $"\"Position\" IN ({CanonicalLaneList}, '')"));
    }

    private static string CanonicalLane(string column) => $"\"{column}\" IN ({CanonicalLaneList})";

    private static bool CarriesWinsAndGames(IMutableEntityType entityType)
        => entityType.FindProperty(WinsColumn)?.ClrType == typeof(int)
            && entityType.FindProperty(GamesColumn)?.ClrType == typeof(int);
}
