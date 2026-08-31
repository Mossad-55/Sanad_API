using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Abstractions.Data;

public interface ICaregiversDbContext
{
    DbSet<Caregiver> Caregivers { get; }
    DbSet<Service> Services { get; }
    DbSet<Language> Languages { get; }
    DbSet<Governorate> Governorates { get; }
    DbSet<City> Cities { get; }
    DbSet<Area> Areas { get; }
    DbSet<Specialization> Specializations { get; }
    DbSet<ProfessionalTitle> ProfessionalTitles { get; }
    DbSet<AcademicDegree> AcademicDegrees { get; }

    Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
        int page,
        int pageSize,
        int? status,
        int? type,
        CancellationToken cancellationToken = default);

    Task<int> CountAdminCaregiversAsync(
        int? status,
        int? type,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}