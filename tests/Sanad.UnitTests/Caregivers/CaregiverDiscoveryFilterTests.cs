using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

/// <summary>
/// Discovery location filters. The search query must thread
/// <c>governorateId</c> and <c>cityId</c> into the data layer (they were carried
/// on the query but previously dropped before the SQL), alongside the existing
/// <c>areaId</c> filter, so the app's governorate/city scope is actually
/// enforced.
/// </summary>
public sealed class CaregiverDiscoveryFilterTests
{
    [Fact]
    public async Task Search_threads_the_governorate_city_and_area_filters_into_the_data_layer()
    {
        var recorded = new RecordingCaregiversDbContext();
        var governorateId = Guid.CreateVersion7();
        var cityId = Guid.CreateVersion7();
        var areaId = Guid.CreateVersion7();

        var result = await new SearchCaregiversQueryHandler(recorded).Handle(
            new SearchCaregiversQuery(
                GovernorateId: governorateId,
                CityId: cityId,
                AreaId: areaId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(governorateId, recorded.GovernorateId);
        Assert.Equal(cityId, recorded.CityId);
        Assert.Equal(areaId, recorded.AreaId);
    }

    [Fact]
    public async Task Search_leaves_the_location_filters_null_when_unset()
    {
        var recorded = new RecordingCaregiversDbContext();

        var result = await new SearchCaregiversQueryHandler(recorded).Handle(
            new SearchCaregiversQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(recorded.GovernorateId);
        Assert.Null(recorded.CityId);
        Assert.Null(recorded.AreaId);
    }

    /// <summary>
    /// Records the filters it is handed and returns an empty page; every other
    /// member defers to a fresh in-memory <see cref="CaregiversDbContext"/> so
    /// the fake stays valid as the interface evolves.
    /// </summary>
    private sealed class RecordingCaregiversDbContext : ICaregiversDbContext
    {
        private readonly CaregiversDbContext _inner = new(
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        public Guid? GovernorateId { get; private set; }
        public Guid? CityId { get; private set; }
        public Guid? AreaId { get; private set; }

        public DbSet<Caregiver> Caregivers => _inner.Caregivers;
        public DbSet<CaregiverRating> CaregiverRatings => _inner.CaregiverRatings;
        public DbSet<CaregiverRatingModerationAction> RatingModerationActions => _inner.RatingModerationActions;
        public DbSet<CaregiverReviewNote> ReviewNotes => _inner.ReviewNotes;
        public DbSet<Service> Services => _inner.Services;
        public DbSet<Language> Languages => _inner.Languages;
        public DbSet<Bank> Banks => _inner.Banks;
        public DbSet<CaregiverPayoutAccount> PayoutAccounts => _inner.PayoutAccounts;
        public DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews => _inner.PayoutAccountReviews;
        public DbSet<CaregiverPayout> Payouts => _inner.Payouts;
        public DbSet<Governorate> Governorates => _inner.Governorates;
        public DbSet<City> Cities => _inner.Cities;
        public DbSet<Area> Areas => _inner.Areas;
        public DbSet<Specialization> Specializations => _inner.Specializations;
        public DbSet<ProfessionalTitle> ProfessionalTitles => _inner.ProfessionalTitles;
        public DbSet<AcademicDegree> AcademicDegrees => _inner.AcademicDegrees;

        public Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
            string? search, int? type, int? gender, Guid? governorateId, Guid? cityId, Guid? areaId,
            Guid? specializationId, int? availability, decimal? minPrice, decimal? maxPrice,
            decimal? minRating, int? minExperienceYears, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            GovernorateId = governorateId;
            CityId = cityId;
            AreaId = areaId;
            return Task.FromResult<(IReadOnlyList<CaregiverSearchCardResponse>, int)>(([], 0));
        }

        public Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize, CancellationToken cancellationToken = default) =>
            _inner.SearchActiveMedicalCaregiversAsync(search, page, pageSize, cancellationToken);

        public Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
            int page, int pageSize, int? status, int? type, CancellationToken cancellationToken = default) =>
            _inner.GetAdminCaregiversAsync(page, pageSize, status, type, cancellationToken);

        public Task<int> CountAdminCaregiversAsync(
            int? status, int? type, CancellationToken cancellationToken = default) =>
            _inner.CountAdminCaregiversAsync(status, type, cancellationToken);

        public Task<(IReadOnlyList<AdminCaregiverLookupItem> Items, int TotalCount)> GetAdminCaregiverLookupAsync(
            string? search, int page, int pageSize, CancellationToken cancellationToken = default) =>
            _inner.GetAdminCaregiverLookupAsync(search, page, pageSize, cancellationToken);

        public Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
            Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId userId,
            CancellationToken cancellationToken = default) =>
            _inner.GetCaregiverUserHeaderAsync(userId, cancellationToken);

        public Task<string?> GetDiscoverableCaregiverAvatarKeyAsync(
            Sanad.BuildingBlocks.Domain.Primitives.Ids.CaregiverId caregiverId,
            CancellationToken cancellationToken = default) =>
            _inner.GetDiscoverableCaregiverAvatarKeyAsync(caregiverId, cancellationToken);

        public Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
            Sanad.BuildingBlocks.Domain.Primitives.Ids.CaregiverId caregiverId, int expectedRevision,
            CaregiverPayout payout, CancellationToken cancellationToken = default) =>
            _inner.SavePayoutIfAccountRevisionVerifiedAsync(caregiverId, expectedRevision, payout, cancellationToken);

        public Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
            CancellationToken cancellationToken = default) =>
            _inner.GetTopRatedCaregiversAsync(cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            _inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _inner.SaveChangesAsync(cancellationToken);
    }
}
