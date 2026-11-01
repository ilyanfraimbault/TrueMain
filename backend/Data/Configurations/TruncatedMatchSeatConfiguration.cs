using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class TruncatedMatchSeatConfiguration : IEntityTypeConfiguration<TruncatedMatchSeat>
{
    public void Configure(EntityTypeBuilder<TruncatedMatchSeat> entity)
    {
        entity.ToTable("truncated_match_seats");

        entity.HasKey(e => new { e.MatchId, e.ParticipantId });

        entity.Property(e => e.MatchId).IsRequired().HasMaxLength(32);
        entity.Property(e => e.PlatformId).IsRequired().HasMaxLength(8);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(32);
        entity.Property(e => e.GameVersion).IsRequired().HasMaxLength(32);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20);
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(32);
        entity.Property(e => e.Puuid).IsRequired().HasMaxLength(128);
        entity.Property(e => e.SkillOrderKey).IsRequired().HasMaxLength(64);

        // The matchup and draft reads seek a champion at a position on a patch, then join
        // the opponent's seat of the same match.
        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition, e.Patch })
            .HasDatabaseName("IX_truncated_match_seats_champion_position_patch");

        // A profile lists one account's games past match retention (#2358).
        entity.HasIndex(e => e.RiotAccountId)
            .HasFilter("\"RiotAccountId\" IS NOT NULL")
            .HasDatabaseName("IX_truncated_match_seats_account");

        // The window purge deletes by patch.
        entity.HasIndex(e => e.Patch).HasDatabaseName("IX_truncated_match_seats_patch");
    }
}
