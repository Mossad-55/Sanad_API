using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Abstractions.Data;

public interface ICaregiversDbContext
{
    DbSet<Caregiver> Caregivers { get; }
    DbSet<CaregiverRating> CaregiverRatings { get; }
    DbSet<CaregiverRatingModerationAction> RatingModerationActions { get; }
    DbSet<CaregiverReviewNote> ReviewNotes { get; }
    DbSet<Service> Services { get; }
    DbSet<Language> Languages { get; }
    DbSet<Bank> Banks { get; }
    DbSet<CaregiverPayoutAccount> PayoutAccounts { get; }
    DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews { get; }
    DbSet<CaregiverPayout> Payouts { get; }

    // Active medical caregivers for the Family medical-access recipient picker.
    // Intentionally independent of discovery privacy filters: grant eligibility
    // depends only on caregiver type and status.
    Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
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

    // Admin caregiver name+id lookup. Names live in identity.users, so this is
    // a raw parameterized SQL read like the admin list above. The Identity
    // join is INNER on purpose: a caregiver row without a linked Identity user
    // cannot satisfy a name lookup and is excluded rather than deleted. Only
    // the two name columns are searched and projected — never phone or email.
    Task<(IReadOnlyList<AdminCaregiverLookupItem> Items, int TotalCount)> GetAdminCaregiverLookupAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Dynamic Search Method querying caregivers and joining identity.users
    Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
        string? search,
        int? type,
        int? gender,
        Guid? governorateId,
        Guid? cityId,
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

    // Resolves the stored Identity avatar key of a caregiver that the public
    // discovery predicate would return (Active status, show_profile not
    // disabled, identity join). Returns null when the caregiver is unknown,
    // not discoverable or has no stored avatar, so the authorized photo read
    // answers a generic 404 without exposing the raw private storage key.
    Task<string?> GetDiscoverableCaregiverAvatarKeyAsync(
        CaregiverId caregiverId,
        CancellationToken cancellationToken = default);

    Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
        CaregiverId caregiverId,
        int expectedRevision,
        CaregiverPayout payout,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
        CancellationToken cancellationToken = default);

    // Runs a multi-save unit of work inside a single relational transaction,
    // committing exactly once when the callback returns and rolling the whole
    // unit back on ANY failure (a later save, an optimistic-concurrency loss or
    // cancellation). Under the InMemory provider there is no transaction to
    // roll back, so this is a passthrough there and atomicity itself is proven
    // against real Postgres. Used by the two-phase certificate promotion, which
    // must not leave a zero-current qualification behind if its second save
    // fails.
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}

public enum PayoutRecordSaveResult
{
    Saved,
    AccountNotFound,
    AccountNotVerified,
    RevisionConflict
}

public sealed record CaregiverUserHeader(
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    Gender? Gender,
    string? AvatarUrl);
