using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain;

public class BestCrushDbContext : DbContext
{
    public BestCrushDbContext(DbContextOptions<BestCrushDbContext> options) : base(options) { }

    public DbSet<Upgrade> Upgrades { get; set; }

    public DbSet<Equipment> Equipments { get; set; }
    public DbSet<EquipmentRecipeEntry> EquipmentRecipeEntries { get; set; }
    public DbSet<Rune> Runes { get; set; }
    public DbSet<Resource> Resources { get; set; }
    public DbSet<MarketPriceObservation> MarketPriceObservations { get; set; }
    public DbSet<CoefficientObservation> CoefficientObservations { get; set; }

    public DbSet<CrushHistorySession> CrushHistorySessions { get; set; }
    public DbSet<CrushHistoryEquipment> CrushHistoryEquipments { get; set; }
    public DbSet<CrushHistoryRune> CrushHistoryRunes { get; set; }
    public DbSet<CrushHistoryRuneLot> CrushHistoryRuneLots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Equipment>().HasMany(e => e.Characteristics).WithOne(e => e.Equipment).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Equipment>().HasMany(e => e.Recipe).WithOne(e => e.Equipment).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Equipment>().HasMany(e => e.EquipmentRecipe).WithOne(e => e.Equipment).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<EquipmentRecipeEntry>().HasOne(e => e.IngredientEquipment).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CoefficientObservation>()
        .HasIndex(c => new
        {
            c.DofusDbId,
            c.ServerName,
            c.ObservedAtUtc
        });
        modelBuilder.Entity<CrushHistorySession>()
        .HasMany(session => session.Equipments)
        .WithOne(equipment => equipment.Session)
        .HasForeignKey(equipment => equipment.SessionId)
        .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CrushHistorySession>()
        .HasMany(session => session.Runes)
        .WithOne(rune => rune.Session)
        .HasForeignKey(rune => rune.SessionId)
        .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CrushHistoryRune>()
        .HasMany(rune => rune.Lots)
        .WithOne(lot => lot.Rune)
        .HasForeignKey(lot => lot.RuneId)
        .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CrushHistorySession>()
        .HasIndex(session => new
        {
            session.ServerName,
            session.CompletedAtUtc
        });

        modelBuilder.Entity<CrushHistoryEquipment>()
        .HasIndex(equipment => new
        {
            equipment.SessionId,
            equipment.DofusDbId
        });

        modelBuilder.Entity<CrushHistoryRune>()
        .HasIndex(rune => new
        {
            rune.SessionId,
            rune.DofusDbId
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

        modelBuilder.Entity<MarketPriceObservation>()
        .HasIndex(p => new
        {
            p.ObjectType,
            p.DofusDbId,
            p.ServerName,
            p.Quantity,
            p.Source,
            p.ObservedAtUtc
        });
    }
}
