using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchBanConfiguration : IEntityTypeConfiguration<MatchBan>
{
    public void Configure(EntityTypeBuilder<MatchBan> entity)
    {
        entity.ToTable("match_bans");

        // Natural-key PK: a ban is identified by where it sits in the draft, and at
        // ten rows per match a surrogate id would cost more than it buys. Also the
        // re-ingestion guard — a match re-read from Riot cannot double-insert its
        // own bans.
        entity.HasKey(e => new { e.MatchId, e.TeamId, e.PickTurn });

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.TeamId)
            .IsRequired();

        entity.Property(e => e.PickTurn)
            .IsRequired();

        entity.Property(e => e.ChampionId)
            .IsRequired();

        // Hard FK to matches, shadow-side only (no navigation, as with every other
        // match child table) so a match delete cascades its bans away and retention
        // needs no extra arm.
        entity.HasOne<Match>()
            .WithMany()
            .HasForeignKey(e => e.MatchId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // The aggregation reads bans by match batch; the PK's leading MatchId column
        // already serves that seek, so no secondary index is declared.
    }
}
