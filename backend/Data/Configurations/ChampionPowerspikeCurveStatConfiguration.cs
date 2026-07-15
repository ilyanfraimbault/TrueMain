using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionPowerspikeCurveStatConfiguration : IEntityTypeConfiguration<ChampionPowerspikeCurveStat>
{
    public void Configure(EntityTypeBuilder<ChampionPowerspikeCurveStat> entity)
    {
        entity.ToTable("champion_powerspike_curve_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20).HasColumnName("elo_bracket").HasDefaultValue(string.Empty);
        entity.Property(e => e.IntervalMinute).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.TotalGoldDiff).IsRequired();
        entity.Property(e => e.TotalDamageDiff).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain: the read seeks on the leading
        // (ChampionId, TeamPosition), optionally narrows to a set of bands, folds
        // intervals to the requested patch scope, and is the ON CONFLICT target for
        // the incremental additive upsert.
        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition, e.Patch, e.EloBracket, e.IntervalMinute })
            .IsUnique();
    }
}
