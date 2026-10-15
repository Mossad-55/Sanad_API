using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sanad.GeographyImport;

public abstract class LookupEntity
{
    public Guid Id { get; set; }
    public string ArabicName { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
public sealed class GovernorateEntity : LookupEntity;
public sealed class CityEntity : LookupEntity { public Guid GovernorateId { get; set; } }
public sealed class AreaEntity : LookupEntity { public Guid CityId { get; set; } }

// Only existing lookup tables: no migration history, startup jobs or other modules.
public sealed class LocationDbContext(DbContextOptions<LocationDbContext> options) : DbContext(options)
{
    public DbSet<GovernorateEntity> Governorates => Set<GovernorateEntity>();
    public DbSet<CityEntity> Cities => Set<CityEntity>();
    public DbSet<AreaEntity> Areas => Set<AreaEntity>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Ignore<LookupEntity>();
        Configure(model.Entity<GovernorateEntity>(), "governorates");
        Configure(model.Entity<CityEntity>(), "cities");
        Configure(model.Entity<AreaEntity>(), "areas");
        model.Entity<CityEntity>().Property(row => row.GovernorateId).HasColumnName("governorate_id");
        model.Entity<AreaEntity>().Property(row => row.CityId).HasColumnName("city_id");
    }
    private static void Configure<T>(EntityTypeBuilder<T> builder, string table) where T : LookupEntity
    {
        builder.HasBaseType((Type?)null);
        builder.ToTable(table, "caregivers");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.ArabicName).HasColumnName("arabic_name").HasMaxLength(150).IsRequired();
        builder.Property(row => row.EnglishName).HasColumnName("english_name").HasMaxLength(150).IsRequired();
        builder.Property(row => row.IsActive).HasColumnName("is_active");
        builder.Property(row => row.CreatedOnUtc).HasColumnName("created_on_utc");
        builder.Property(row => row.UpdatedOnUtc).HasColumnName("updated_on_utc");
    }
}
