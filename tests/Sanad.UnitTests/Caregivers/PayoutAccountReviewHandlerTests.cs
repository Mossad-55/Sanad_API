using System.Text;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Abstractions.Security;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

public sealed class PayoutAccountReviewHandlerTests
{
    [Fact]
    public async Task Approve_ShouldVerifyPendingAccountAndAuditHistory()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);
        var actor = UserId.New();

        var result =
            await new ApprovePayoutAccountCommandHandler(dbContext).Handle(
                new ApprovePayoutAccountCommand(
                    caregiver.Id.Value,
                    actor,
                    1,
                    "BankPortal",
                    "REF-1",
                    Utc(2)),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Verified", result.Value.Status);
        Assert.Equal(actor.Value, result.Value.VerifiedBy);
        Assert.Equal(Utc(2), result.Value.VerifiedOnUtc);
        Assert.Equal("BankPortal", result.Value.VerificationSource);
        Assert.Equal("REF-1", result.Value.Reference);

        var review = Assert.Single(await dbContext.PayoutAccountReviews.ToListAsync());
        Assert.Equal(PayoutAccountReviewDecision.Verified, review.Decision);
        Assert.Equal(1, review.AccountRevision);
        Assert.Equal(actor, review.ActorUserId);
        Assert.Equal("BankPortal", review.VerificationSource);
        Assert.Equal("REF-1", review.Reference);
    }

    [Fact]
    public async Task Approve_ShouldRejectStaleRevision()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);

        var result =
            await new ApprovePayoutAccountCommandHandler(dbContext).Handle(
                new ApprovePayoutAccountCommand(
                    caregiver.Id.Value,
                    UserId.New(),
                    999,
                    "BankPortal",
                    null,
                    Utc(2)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.RevisionConflict, result.Error);
        Assert.Equal(0, await dbContext.PayoutAccountReviews.CountAsync());
    }

    [Fact]
    public async Task Approve_ShouldRejectNonPendingAccount()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);
        var approve = new ApprovePayoutAccountCommandHandler(dbContext);
        var command = new ApprovePayoutAccountCommand(
            caregiver.Id.Value, UserId.New(), 1, "BankPortal", null, Utc(2));
        Assert.True((await approve.Handle(command, default)).IsSuccess);

        var repeat = await approve.Handle(command with { UtcNow = Utc(3) }, default);

        Assert.True(repeat.IsFailure);
        Assert.Equal(PayoutAccountErrors.InvalidState, repeat.Error);
    }

    [Fact]
    public async Task Reject_ShouldMovePendingAccountToRejected()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);
        var actor = UserId.New();

        var result =
            await new RejectPayoutAccountCommandHandler(dbContext).Handle(
                new RejectPayoutAccountCommand(
                    caregiver.Id.Value,
                    actor,
                    1,
                    "Name mismatch",
                    Utc(2)),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", result.Value.Status);
        Assert.Equal("Name mismatch", result.Value.RejectionReason);
        Assert.Equal(actor.Value, result.Value.ReviewedBy);

        var review = Assert.Single(await dbContext.PayoutAccountReviews.ToListAsync());
        Assert.Equal(PayoutAccountReviewDecision.Rejected, review.Decision);
        Assert.Equal("Name mismatch", review.Reason);
    }

    [Fact]
    public async Task Revoke_ShouldMoveVerifiedAccountToRevoked()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);
        var approve = new ApprovePayoutAccountCommandHandler(dbContext);
        Assert.True((await approve.Handle(
            new ApprovePayoutAccountCommand(
                caregiver.Id.Value, UserId.New(), 1, "BankPortal", null, Utc(2)),
            default)).IsSuccess);

        var result =
            await new RevokePayoutAccountCommandHandler(dbContext).Handle(
                new RevokePayoutAccountCommand(
                    caregiver.Id.Value,
                    UserId.New(),
                    2,
                    "Fraud suspected",
                    Utc(3)),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Revoked", result.Value.Status);
        Assert.Equal("Fraud suspected", result.Value.RejectionReason);

        Assert.Equal(2, await dbContext.PayoutAccountReviews.CountAsync());
    }

    [Fact]
    public async Task Revoke_ShouldRejectPendingAccount()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);

        var result =
            await new RevokePayoutAccountCommandHandler(dbContext).Handle(
                new RevokePayoutAccountCommand(
                    caregiver.Id.Value,
                    UserId.New(),
                    1,
                    "Fraud suspected",
                    Utc(2)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.InvalidState, result.Error);
    }

    [Fact]
    public async Task Decision_ShouldReturnNotFound_WhenNoAccountSaved()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);

        var result =
            await new ApprovePayoutAccountCommandHandler(dbContext).Handle(
                new ApprovePayoutAccountCommand(
                    caregiver.Id.Value,
                    UserId.New(),
                    1,
                    "BankPortal",
                    null,
                    Utc(2)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Reveal_ShouldReturnFullIbanAndAuditEvent()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);
        var actor = UserId.New();

        var result =
            await new RevealPayoutAccountIbanCommandHandler(
                dbContext,
                new RoundTripIbanProtector()).Handle(
                new RevealPayoutAccountIbanCommand(
                    caregiver.Id.Value,
                    actor,
                    Utc(2)),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("GB29NWBK60161331926819", result.Value.Iban);
        Assert.Equal("****6819", result.Value.MaskedIban);

        var review = Assert.Single(await dbContext.PayoutAccountReviews.ToListAsync());
        Assert.Equal(PayoutAccountReviewDecision.Revealed, review.Decision);
        Assert.Equal(actor, review.ActorUserId);
        Assert.Equal(1, review.AccountRevision);
    }

    [Fact]
    public async Task Reveal_ShouldFailClosed_WhenProtectionUnavailable()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);

        var result =
            await new RevealPayoutAccountIbanCommandHandler(
                dbContext,
                new UnavailableIbanProtector()).Handle(
                new RevealPayoutAccountIbanCommand(
                    caregiver.Id.Value,
                    UserId.New(),
                    Utc(2)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.ProtectionUnavailable, result.Error);
        Assert.Equal(0, await dbContext.PayoutAccountReviews.CountAsync());
    }

    [Fact]
    public async Task List_ShouldOrderPendingFirstWithMaskedIban()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver first = await AddCaregiverAsync(inner);
        Caregiver second = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, first.Id);
        await AddVerifiedAccountAsync(inner, second.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, first.UserId);

        var result =
            await new GetPayoutAccountsQueryHandler(dbContext).Handle(
                new GetPayoutAccountsQuery(null, 1, 20),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal("Pending", result.Value.Items[0].Status);
        Assert.Equal("****6819", result.Value.Items[0].MaskedIban);
        Assert.Equal("Verified", result.Value.Items[1].Status);

        var pendingOnly = await new GetPayoutAccountsQueryHandler(dbContext).Handle(
            new GetPayoutAccountsQuery(PayoutAccountStatus.Pending, 1, 20),
            default);

        Assert.True(pendingOnly.IsSuccess);
        Assert.Equal(1, pendingOnly.Value.TotalCount);
    }

    [Fact]
    public async Task Reviews_ShouldListDecisionsNewestFirst()
    {
        using CaregiversDbContext inner = CreateDbContext();
        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPendingAccountAsync(inner, caregiver.Id);
        var dbContext = new FixedHeaderCaregiversDbContext(inner, caregiver.UserId);
        var actor = UserId.New();
        Assert.True((await new ApprovePayoutAccountCommandHandler(dbContext).Handle(
            new ApprovePayoutAccountCommand(
                caregiver.Id.Value, actor, 1, "BankPortal", null, Utc(2)),
            default)).IsSuccess);
        Assert.True((await new RevokePayoutAccountCommandHandler(dbContext).Handle(
            new RevokePayoutAccountCommand(
            caregiver.Id.Value, actor, 2, "Fraud suspected", Utc(3)),
            default)).IsSuccess);

        var result =
            await new GetPayoutAccountReviewsQueryHandler(dbContext).Handle(
                new GetPayoutAccountReviewsQuery(caregiver.Id.Value),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Revoked", result.Value[0].Decision);
        Assert.Equal("Verified", result.Value[1].Decision);
        Assert.Equal("Fraud suspected", result.Value[0].Reason);
        Assert.Equal(actor.Value, result.Value[0].ActorUserId);
    }

    private static async Task<Caregiver> AddCaregiverAsync(
        CaregiversDbContext dbContext)
    {
        Caregiver caregiver =
            Caregiver.Create(UserId.New(), CaregiverType.Medical);

        dbContext.Caregivers.Add(caregiver);
        await dbContext.SaveChangesAsync();

        return caregiver;
    }

    private static async Task AddPendingAccountAsync(
        CaregiversDbContext dbContext,
        CaregiverId caregiverId)
    {
        dbContext.PayoutAccounts.Add(
            CaregiverPayoutAccount.Create(
                caregiverId,
                "Mohamed Ahmed",
                "NBE",
                "v1.test.GB29NWBK60161331926819",
                "6819"));

        await dbContext.SaveChangesAsync();
    }

    private static async Task AddVerifiedAccountAsync(
        CaregiversDbContext dbContext,
        CaregiverId caregiverId)
    {
        var account = CaregiverPayoutAccount.Create(
            caregiverId,
            "Mohamed Ahmed",
            "NBE",
            "v1.test.GB29NWBK60161331926819",
            "6819");

        account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
        dbContext.PayoutAccounts.Add(account);
        await dbContext.SaveChangesAsync();
    }

    private static CaregiversDbContext CreateDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ApproveValidator_ShouldRequireSource()
    {
        var validator = new ApprovePayoutAccountCommandValidator();

        var missing = validator.Validate(
            new ApprovePayoutAccountCommand(
                Guid.CreateVersion7(), UserId.New(), 1, "", null, Utc(2)));

        Assert.False(missing.IsValid);

        var valid = validator.Validate(
            new ApprovePayoutAccountCommand(
                Guid.CreateVersion7(), UserId.New(), 1, "BankPortal", null, Utc(2)));

        Assert.True(valid.IsValid);
    }

    [Fact]
    public void RejectValidator_ShouldRequireReason()
    {
        var validator = new RejectPayoutAccountCommandValidator();

        var missing = validator.Validate(
            new RejectPayoutAccountCommand(
                Guid.CreateVersion7(), UserId.New(), 1, "  ", Utc(2)));

        Assert.False(missing.IsValid);

        var valid = validator.Validate(
            new RejectPayoutAccountCommand(
                Guid.CreateVersion7(), UserId.New(), 1, "Name mismatch", Utc(2)));

        Assert.True(valid.IsValid);
    }

    private sealed class FixedHeaderCaregiversDbContext(
        ICaregiversDbContext inner,
        UserId userId) : ICaregiversDbContext
    {
        public DbSet<Caregiver> Caregivers => inner.Caregivers;
        public DbSet<CaregiverRating> CaregiverRatings => inner.CaregiverRatings;
        public DbSet<Service> Services => inner.Services;
        public DbSet<Language> Languages => inner.Languages;
        public DbSet<Bank> Banks => inner.Banks;
        public DbSet<CaregiverPayoutAccount> PayoutAccounts => inner.PayoutAccounts;
        public DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews => inner.PayoutAccountReviews;
        public DbSet<CaregiverPayout> Payouts => inner.Payouts;
        public DbSet<Governorate> Governorates => inner.Governorates;
        public DbSet<City> Cities => inner.Cities;
        public DbSet<Area> Areas => inner.Areas;
        public DbSet<Specialization> Specializations => inner.Specializations;
        public DbSet<ProfessionalTitle> ProfessionalTitles => inner.ProfessionalTitles;
        public DbSet<AcademicDegree> AcademicDegrees => inner.AcademicDegrees;

        public Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
            int page, int pageSize, int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiversAsync(page, pageSize, status, type, cancellationToken);

        public Task<int> CountAdminCaregiversAsync(
            int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.CountAdminCaregiversAsync(status, type, cancellationToken);

        public Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
            string? search, int? type, int? gender, Guid? areaId, Guid? specializationId,
            int? availability, decimal? minPrice, decimal? maxPrice, decimal? minRating,
            int? minExperienceYears, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveCaregiversAsync(
                search, type, gender, areaId, specializationId, availability,
                minPrice, maxPrice, minRating, minExperienceYears, page, pageSize,
                cancellationToken);

        public Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveMedicalCaregiversAsync(search, page, pageSize, cancellationToken);

        public Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
            UserId requestedUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CaregiverUserHeader?>(
                new CaregiverUserHeader(
                    userId.Value,
                    "محمد أحمد",
                    "Mohamed Ahmed",
                    null,
                    null));

        public Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetTopRatedCaregiversAsync(cancellationToken);

        public Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
            CaregiverId caregiverId, int expectedRevision, CaregiverPayout payout,
            CancellationToken cancellationToken = default) =>
            inner.SavePayoutIfAccountRevisionVerifiedAsync(
                caregiverId, expectedRevision, payout, cancellationToken);

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class RoundTripIbanProtector : IIbanProtector
    {
        public string Protect(
            string plaintextIban) =>
            "v1.test." + plaintextIban;

        public string Unprotect(
            string envelope) =>
            envelope.StartsWith("v1.test.", StringComparison.Ordinal)
                ? envelope["v1.test.".Length..]
                : throw new IbanProtectionException();
    }

    private sealed class UnavailableIbanProtector : IIbanProtector
    {
        public string Protect(
            string plaintextIban) =>
            throw new IbanProtectionException();

        public string Unprotect(
            string envelope) =>
            throw new IbanProtectionException();
    }
}
