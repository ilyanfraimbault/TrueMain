using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class TrueMainDbContext : DbContext
{
    public TrueMainDbContext(DbContextOptions<TrueMainDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("players");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.GameName)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.TagLine)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(e => e.Region)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(e => e.Puuid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("now()");

            entity.HasIndex(e => e.Puuid)
                .IsUnique();

            entity.HasIndex(e => new { e.GameName, e.TagLine, e.Region })
                .IsUnique();
        });
    }
}
