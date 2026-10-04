using AwesomeAssertions;
using Ingestor.Processes.Components.IncrementalFolds;

namespace TrueMain.UnitTests;

/// <summary>
/// The statement the incremental folds' additive upserts are generated into (#1239).
/// Pinned verbatim against the hand-written shape each fold used to carry, so a change to
/// the generator shows up as a diff of the SQL every aggregate table receives.
/// </summary>
public sealed class AdditiveUpsertTests
{
    [Fact]
    public void Sql_InsertsFromUnnest_AndAddsEveryCounterOnConflict()
    {
        var upsert = new AdditiveUpsert<(int Champion, string Patch, int Bans, long Gold)>("champion_ban_stats")
            .Key("ChampionId", "integer", r => r.Champion)
            .Key("elo_bracket", "text", r => r.Patch)
            .Sum("Bans", "integer", r => r.Bans)
            .Sum("GoldSum", "bigint", r => r.Gold);

        upsert.Sql.Should().Be(
            """
            INSERT INTO champion_ban_stats
                ("Id", "ChampionId", "elo_bracket", "Bans", "GoldSum", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.c0, t.c1, t.c2, t.c3, @aggAt
            FROM unnest(@c0::integer[], @c1::text[], @c2::integer[], @c3::bigint[])
                AS t(c0, c1, c2, c3)
            ON CONFLICT ("ChampionId", "elo_bracket") DO UPDATE SET
                "Bans" = champion_ban_stats."Bans" + EXCLUDED."Bans",
                "GoldSum" = champion_ban_stats."GoldSum" + EXCLUDED."GoldSum",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """.ReplaceLineEndings());
    }

    [Fact]
    public void Sql_RefusesATableWithoutAKey()
    {
        var upsert = new AdditiveUpsert<int>("t").Sum("Games", "integer", r => r);

        var act = () => upsert.Sql;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sql_RefusesATableWithoutACounter()
    {
        var upsert = new AdditiveUpsert<int>("t").Key("ChampionId", "integer", r => r);

        var act = () => upsert.Sql;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Key_RefusesAColumnDeclaredAfterTheStatementWasBuilt()
    {
        var upsert = new AdditiveUpsert<int>("t")
            .Key("ChampionId", "integer", r => r)
            .Sum("Games", "integer", r => r);
        _ = upsert.Sql;

        var act = () => upsert.Sum("Wins", "integer", r => r);

        act.Should().Throw<InvalidOperationException>();
    }
}
