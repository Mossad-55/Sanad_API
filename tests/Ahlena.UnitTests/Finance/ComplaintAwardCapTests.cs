using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.UnitTests.Finance;

/// <summary>
/// Provisional single-award cap for reviewed complaint awards (Owner-ordered
/// default EGP 500.00 = 50000 piastres, 2026-10-23; configuration
/// <c>Finance:ComplaintReviews:MaxAwardMinorUnits</c>). The cap is an
/// application-seam guard on <c>ApproveComplaintReviewCommand</c>: a refusal
/// records no verdict and appends no ledger entry. An explicit raw configuration
/// value of 0 disables the cap; an absent value selects the provisional default;
/// negative and malformed raw values are rejected at startup (the direct
/// <c>FromConfiguredValue</c> factory retains its non-positive = disabled
/// behavior for pre-parsed callers). These tests pin the option semantics and
/// the handler boundary (equal-to-cap is allowed, above-cap is refused).
/// </summary>
public class ComplaintAwardCapTests
{
    private const string SuperAdmin = ComplaintReviewRoleGuard.SuperAdminAccountType;
    private static readonly DateTime Now = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, 50_000L)]      // absent configuration -> provisional default
    [InlineData(50_000L, 50_000L)]   // explicit equal value
    [InlineData(80_000L, 80_000L)]   // owner-raised value
    [InlineData(0L, null)]          // explicit disable
    [InlineData(-1L, null)]         // direct option disables; config parser rejects negative
    public void Options_FromConfiguredValue_SelectsCapAsDocumented(
        long? configured,
        long? expectedCap)
    {
        var options = ComplaintAwardOptions.FromConfiguredValue(configured);
        Assert.Equal(expectedCap, options.MaxAwardMinorUnits);
    }

    [Fact]
    public void Options_ParseConfiguredValue_RejectsMalformedAndNegativeValues()
    {
        Assert.Throws<InvalidOperationException>(
            () => ComplaintAwardOptions.ParseConfiguredValue("not-a-number"));
        Assert.Throws<InvalidOperationException>(
            () => ComplaintAwardOptions.ParseConfiguredValue("-1"));
        Assert.Equal(0L, ComplaintAwardOptions.ParseConfiguredValue("0"));
        Assert.Null(ComplaintAwardOptions.ParseConfiguredValue(null));
    }

    [Fact]
    public async Task ZeroAward_IsValidUnderDefaultCap()
    {
        var store = new FakeStore();
        var handler = new ApproveComplaintReviewCommandHandler(
            store, Options.Create(ComplaintAwardOptions.FromConfiguredValue(null)));

        var result = await handler.Handle(
            new ApproveComplaintReviewCommand(
                Guid.NewGuid(), Guid.NewGuid(), SuperAdmin, Now, 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0L, store.ForwardedAward);
        Assert.Equal(1, store.ApproveCalls);
    }

    [Fact]
    public async Task Award_EqualToCap_IsForwardedToTheStore()
    {
        var store = new FakeStore();
        var handler = new ApproveComplaintReviewCommandHandler(
            store, Options.Create(ComplaintAwardOptions.FromConfiguredValue(50_000)));

        var result = await handler.Handle(
            new ApproveComplaintReviewCommand(
                Guid.NewGuid(), Guid.NewGuid(), SuperAdmin, Now, 50_000),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50_000, store.ForwardedAward);
        Assert.Equal(1, store.ApproveCalls);
    }

    [Fact]
    public async Task Award_AboveCap_IsRefusedWithoutRecordingAnything()
    {
        var store = new FakeStore();
        var handler = new ApproveComplaintReviewCommandHandler(
            store, Options.Create(ComplaintAwardOptions.FromConfiguredValue(50_000)));

        var result = await handler.Handle(
            new ApproveComplaintReviewCommand(
                Guid.NewGuid(), Guid.NewGuid(), SuperAdmin, Now, 50_001),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ComplaintReviewErrors.AwardExceedsCap, result.Error);
        Assert.Equal(0, store.ApproveCalls);
        Assert.Null(store.ForwardedAward);
    }

    [Fact]
    public async Task ExplicitlyDisabledCap_AdmitsAnyAward()
    {
        var store = new FakeStore();
        var handler = new ApproveComplaintReviewCommandHandler(
            store, Options.Create(ComplaintAwardOptions.FromConfiguredValue(0)));

        var result = await handler.Handle(
            new ApproveComplaintReviewCommand(
                Guid.NewGuid(), Guid.NewGuid(), SuperAdmin, Now, long.MaxValue),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(long.MaxValue, store.ForwardedAward);
    }

    private sealed class FakeStore : IComplaintReviewStore
    {
        public int ApproveCalls { get; private set; }
        public long? ForwardedAward { get; private set; }

        public Task<Result<ComplaintReviewSnapshot>> OpenAsync(
            Guid refundCaseId, Guid familyId, ComplaintReportingParty reportingParty,
            Guid reportedByUserId, DateTime reportedOnUtc, DateTime? visitedOnUtc,
            bool isSafetyConcern, string? description, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ComplaintReviewSnapshot>.Success(Snapshot()));

        public Task<Result<ComplaintReviewSnapshot>> BeginInvestigationAsync(
            Guid complaintReviewId, Guid investigatorUserId, DateTime startedOnUtc,
            string? evidenceReference, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ComplaintReviewSnapshot>.Success(Snapshot()));

        public Task<Result<ComplaintReviewSnapshot>> ApproveAsync(
            Guid complaintReviewId, Guid approvingUserId, DateTime approvedOnUtc,
            long awardedMinorUnits, string? decisionNote, string? evidenceReference,
            CancellationToken cancellationToken)
        {
            ApproveCalls++;
            ForwardedAward = awardedMinorUnits;
            return Task.FromResult(Result<ComplaintReviewSnapshot>.Success(Snapshot()));
        }

        public Task<Result<ComplaintReviewSnapshot>> RefuseAsync(
            Guid complaintReviewId, Guid refusingUserId, DateTime refusedOnUtc,
            string? decisionNote, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ComplaintReviewSnapshot>.Success(Snapshot()));

        private static ComplaintReviewSnapshot Snapshot() =>
            new(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                ComplaintReportingParty.Family, Guid.NewGuid(), Now, null,
                ComplaintTimeliness.WithinWindow, ComplaintReviewState.Investigating,
                RequiresLateReview: false, Description: null,
                InvestigatorUserId: null, InvestigationStartedOnUtc: null,
                EvidenceReference: null, DecisionNote: null,
                ApprovedByUserId: null, ApprovedOnUtc: null,
                RefusedByUserId: null, RefusedOnUtc: null,
                ProcessingDeadlineStartsOnUtc: null, Now, Now);
    }
}
