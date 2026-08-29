using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence;

public sealed class CaregiversDbContext :
    DbContext,
    ICaregiversDbContext
{
    public const string Schema = "caregivers";

    public CaregiversDbContext(
        DbContextOptions<CaregiversDbContext> options)
        : base(options)
    {
    }

    public DbSet<Caregiver> Caregivers => Set<Caregiver>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Specialization> Specializations => Set<Specialization>();
    public DbSet<ProfessionalTitle> ProfessionalTitles => Set<ProfessionalTitle>();
    public DbSet<AcademicDegree> AcademicDegrees => Set<AcademicDegree>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CaregiversDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}