using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

/// <summary>
/// The item-context triple (#1450). The four enum columns are stored as <b>text</b>, per
/// the enum rule in <c>decisions/backend-conventions.md</c>: these are exactly the columns
/// an operator reads by hand while asking why a card says what it says, and
/// <c>Axis = 'EnemyMagicDamage'</c> beats <c>"Axis" = 1</c> in every psql session that
/// will ever touch them.
/// </summary>
public sealed class ChampionItemContextStatConfiguration : IEntityTypeConfiguration<ChampionItemContextStat>
{
    public void Configure(EntityTypeBuilder<ChampionItemContextStat> entity)
    {
        entity.ToTable("champion_item_context_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Slot).IsRequired().HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.ItemId).IsRequired();
        entity.Property(e => e.Axis).IsRequired().HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.Bucket).IsRequired().HasConversion<string>().HasMaxLength(8);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain and the ON CONFLICT target of the additive upsert.
        // Patch, champion and position lead because every consumer — the verdict builder
        // included — reads one champion's whole slice at a time, which is then one range
        // scan rather than a seek per item.
        entity.HasIndex(e => new
        {
            e.Patch,
            e.ChampionId,
            e.Position,
            e.Slot,
            e.ItemId,
            e.Axis,
            e.Bucket,
        }).IsUnique().HasDatabaseName("IX_champion_item_context_stats_grain");
    }
}

public sealed class ChampionItemContextTotalConfiguration : IEntityTypeConfiguration<ChampionItemContextTotal>
{
    public void Configure(EntityTypeBuilder<ChampionItemContextTotal> entity)
    {
        entity.ToTable("champion_item_context_totals");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Slot).IsRequired().HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.Axis).IsRequired().HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.Bucket).IsRequired().HasConversion<string>().HasMaxLength(8);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        entity.HasIndex(e => new
        {
            e.Patch,
            e.ChampionId,
            e.Position,
            e.Slot,
            e.Axis,
            e.Bucket,
        }).IsUnique().HasDatabaseName("IX_champion_item_context_totals_grain");
    }
}

public sealed class ChampionItemContextVerdictConfiguration : IEntityTypeConfiguration<ChampionItemContextVerdict>
{
    public void Configure(EntityTypeBuilder<ChampionItemContextVerdict> entity)
    {
        entity.ToTable("champion_item_context_verdicts");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Slot).IsRequired().HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.ItemId).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.SlotGames).IsRequired();
        entity.Property(e => e.PickRate).IsRequired();
        entity.Property(e => e.Class).IsRequired().HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.PatchWindow).IsRequired().HasDefaultValue(1);

        // The findings ride as jsonb like every other structured payload in this schema
        // (match_participants.ItemEvents is the precedent): they are read as a whole, per
        // row, and never filtered on in SQL.
        entity.Property(e => e.Axes).HasColumnType("jsonb").IsRequired();

        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // The read's access path: one champion, one position, one patch — every verdict of
        // every slot in one range scan, which is exactly what the page asks for.
        entity.HasIndex(e => new
        {
            e.Patch,
            e.ChampionId,
            e.Position,
            e.Slot,
            e.ItemId,
        }).IsUnique().HasDatabaseName("IX_champion_item_context_verdicts_grain");
    }
}
