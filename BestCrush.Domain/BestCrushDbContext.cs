using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain;

public class BestCrushDbContext : DbContext
{
    public BestCrushDbContext(DbContextOptions<BestCrushDbContext> options) : base(options) { }

    public DbSet<Upgrade> Upgrades { get; set; }

    public DbSet<Equipment> Equipments { get; set; }
    public DbSet<Rune> Runes { get; set; }
    public DbSet<Resource> Resources { get; set; }
    public DbSet<MarketPriceObservation> MarketPriceObservations { get; set; }
    public DbSet<CoefficientObservation> CoefficientObservations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Equipment>().HasMany(e => e.Characteristics).WithOne(e => e.Equipment).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Equipment>().HasMany(e => e.Recipe).WithOne(e => e.Equipment).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CoefficientObservation>()
        .HasIndex(c => new
        {
            c.DofusDbId,
            c.ServerName,
            c.ObservedAtUtc
        });
        modelBuilder.Entity<Equipment>().HasAlternateKey(u => u.DofusDbId);
        modelBuilder.Entity<Rune>().HasAlternateKey(u => u.DofusDbId);
        modelBuilder.Entity<Resource>().HasAlternateKey(u => u.DofusDbId);
        modelBuilder.Entity<MarketPriceObservation>()
        .HasIndex(p => new
        {
            p.ObjectType,
            p.DofusDbId,
            p.ServerName,
            p.Quantity,
            p.ObservedAtUtc
        });
    }
}
