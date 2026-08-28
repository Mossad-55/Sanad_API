using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Abstractions.Data;

public interface ICaregiversDbContext
{
    DbSet<Service> Services { get; }
    DbSet<Language> Languages { get; }
    DbSet<Governorate> Governorates { get; }
    DbSet<City> Cities { get; }
    DbSet<Area> Areas { get; }
    DbSet<Specialization> Specializations { get; }
    DbSet<ProfessionalTitle> ProfessionalTitles { get; }
    DbSet<AcademicDegree> AcademicDegrees { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}