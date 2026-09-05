using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
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

    // Dynamic Search Method querying caregivers and joining identity.users
    Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
        string? search,
        int? type,
        int? gender,
        Guid? areaId,
        Guid? specializationId,
        int? availability,
        decimal? minPrice,
        decimal? maxPrice,
        decimal? minRating,
        int? minExperienceYears,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Dynamic User Header Method querying identity.users
    Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record CaregiverUserHeader(
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    Gender? Gender,
    string? AvatarUrl);