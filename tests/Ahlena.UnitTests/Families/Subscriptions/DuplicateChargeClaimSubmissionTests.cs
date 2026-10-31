using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Application.Subscriptions;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.UnitTests.Families.Subscriptions;

/// <summary>
/// Support-approval migration for family duplicate-charge claims; executed in
/// the combined focused and full Release suites.
/// <para>
/// Submission derives everything server-side — the succeeded capture, the
/// FULL verified basis from the attempt's settled payable, the case id shared
/// with the legacy award derivation — and never awards money. The retired
/// direct-award handler is fail-closed behind its guard, and the submission's
/// probe-safe/ownership codes match the shipped refund contract.
/// </para>
/// </summary>
public sealed class DuplicateChargeClaimSubmissionTests
{
    private static readonly DateTime BaseUtc = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Submission_names_the_full_verified_basis_of_the_duplicated_capture()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        StubClaimStore claims = new();
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        Result<FamilyDuplicateChargeClaimView> result = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(owner, attempt.Id, IdempotencyKey: "dup-key"), default);

        Assert.True(result.IsSuccess);
        // Clause 9: the FULL captured payable, server-derived — never caller-
        // supplied, never prorated.
        Assert.Equal(34086L, claims.LastSubmission!.BasisMinorUnits);
        Assert.Equal(subscription.Id, claims.LastSubmission.SubscriptionId);
        Assert.Equal(attempt.Id, claims.LastSubmission.PaymentAttemptId);
        Assert.Equal(family.Id.Value, claims.LastSubmission.FamilyAccountId);
        Assert.Equal("dup-key", claims.LastSubmission.IdempotencyKey);
        // One case id per corrected charge, byte-identical to the retired
        // derivation, so legacy rows and claim rows serialize on one case.
        Span<byte> source = stackalloc byte[16];
        attempt.Id.TryWriteBytes(source);
        source[0] ^= 0xD0;
        Assert.Equal(new Guid(source), claims.LastSubmission.RefundCaseId);
    }

    [Fact]
    public async Task An_older_periods_money_never_dilutes_the_claim_basis()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));

        SubscriptionPaymentAttempt earlier = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 500m, 0m, 0m, 500m, 14m, 70m, 570m,
            null, SubscriptionPaymentMethod.Card, Utc(1).AddMonths(-1));
        earlier.LinkSubscription(subscription.Id);
        earlier.TryMarkSucceeded("txn-old", Utc(1).AddMonths(-1));

        SubscriptionPaymentAttempt corrected = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        corrected.LinkSubscription(subscription.Id);
        corrected.TryMarkSucceeded("txn-1", Utc(1));

        db.SubscriptionPaymentAttempts.AddRange(earlier, corrected);
        await db.SaveChangesAsync();

        StubClaimStore claims = new();
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        var result = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(owner, corrected.Id), default);

        Assert.True(result.IsSuccess);
        // The capture ceiling, not a lifetime sum: only this attempt's money.
        Assert.Equal(34086L, claims.LastSubmission!.BasisMinorUnits);
    }

    [Fact]
    public async Task A_non_succeeded_attempt_is_probe_safe_and_stores_nothing()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        StubClaimStore claims = new();
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        var result = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(owner, attempt.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.NothingToRefund", result.Error.Code);
        Assert.Null(claims.LastSubmission);
    }

    [Fact]
    public async Task A_non_owner_is_refused_before_any_claim_is_stored()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        StubClaimStore claims = new();
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        var result = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(editor, attempt.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.NotOwner", result.Error.Code);
        Assert.Null(claims.LastSubmission);
    }

    [Fact]
    public async Task A_non_utc_report_instant_is_a_clean_malformed_400()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        StubClaimStore claims = new();
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        var result = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(
                owner, attempt.Id, CustomerReportedOnUtc: DateTime.SpecifyKind(BaseUtc, DateTimeKind.Unspecified)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.MalformedInput", result.Error.Code);
        Assert.Null(claims.LastSubmission);
    }

    [Fact]
    public async Task An_attempt_without_a_subscription_link_is_a_clean_malformed_400()
    {
        // Migrated legacy rule (M4): the subscription is the audit correlation.
        // A succeeded capture with no server-side link cannot be attributed to
        // a subscription, so the claim submission refuses it with the inherited
        // 400 code and claims no refundable capture anonymously.
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        StubClaimStore claims = new();
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        var result = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(owner, attempt.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.MalformedInput", result.Error.Code);
        Assert.Null(claims.LastSubmission);
    }

    [Fact]
    public async Task Finance_claim_errors_map_onto_the_shipped_family_contract()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        // A capture that can no longer fund the full basis maps to the shipped
        // ExceedsCaptured 409: the family contract never leaks Finance codes.
        StubClaimStore claims = new(failWith: "Finance.DuplicateClaim.CaptureBasisUnavailable");
        var handler = new SubmitDuplicateChargeClaimCommandHandler(db, claims, new FixedClock(BaseUtc));

        Result<FamilyDuplicateChargeClaimView> exceeds = await handler.Handle(
            new SubmitDuplicateChargeClaimCommand(owner, attempt.Id), default);
        Assert.True(exceeds.IsFailure);
        Assert.Equal("Subscriptions.Refund.ExceedsCaptured", exceeds.Error.Code);

        StubClaimStore consumed = new(failWith: "Finance.DuplicateClaim.CaptureAlreadyClaimed");
        Result<FamilyDuplicateChargeClaimView> claimed = await new SubmitDuplicateChargeClaimCommandHandler(
            db, consumed, new FixedClock(BaseUtc)).Handle(
            new SubmitDuplicateChargeClaimCommand(owner, attempt.Id), default);
        Assert.True(claimed.IsFailure);
        Assert.Equal("Subscriptions.Refund.CaptureAlreadyClaimed", claimed.Error.Code);

        StubClaimStore conflict = new(failWith: "Finance.DuplicateClaim.IdempotencyConflict");
        Result<FamilyDuplicateChargeClaimView> idempotent = await new SubmitDuplicateChargeClaimCommandHandler(
            db, conflict, new FixedClock(BaseUtc)).Handle(
            new SubmitDuplicateChargeClaimCommand(owner, attempt.Id), default);
        Assert.True(idempotent.IsFailure);
        Assert.Equal("Subscriptions.Refund.IdempotencyConflict", idempotent.Error.Code);
    }

    private static FamilySubscription SeedSubscriptionFor(
        FamiliesDbContext db,
        Family family,
        SubscriptionPlanVersion plan,
        DateTime createdOnUtc)
    {
        FamilySubscription subscription = FamilySubscription.Create(
            family.Id, plan, createdOnUtc, createdOnUtc.AddMonths(1));
        db.FamilySubscriptions.Add(subscription);
        return subscription;
    }

    private static FamiliesDbContext CreateContext() => new(new DbContextOptionsBuilder<FamiliesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class StubClaimStore(string? failWith = null) : IDuplicateChargeClaimStore
    {
        public DuplicateChargeClaimSubmissionInput? LastSubmission { get; private set; }

        public Task<Result<FamilyDuplicateChargeClaimView>> SubmitAsync(
            DuplicateChargeClaimSubmissionInput input,
            CancellationToken cancellationToken)
        {
            LastSubmission = input;

            if (failWith is not null)
            {
                return Task.FromResult(Result<FamilyDuplicateChargeClaimView>.Failure(new Error(failWith, "rejected")));
            }

            return Task.FromResult(Result<FamilyDuplicateChargeClaimView>.Success(new FamilyDuplicateChargeClaimView(
                Guid.NewGuid(), input.RefundCaseId, input.PaymentAttemptId, input.SubscriptionId,
                input.BasisMinorUnits, "EGP", DuplicateChargeClaimState.Submitted, "pending",
                null, input.SubmittedOnUtc, null, IsReplay: false)));
        }

        public Task<Result<DuplicateChargeClaimAdminView>> ApproveAsync(
            Guid claimId, Guid decidedByAccountId, string? decidedByAccountType,
            DateTime decidedOnUtc, string verifiedBasis, string? decisionNote,
            string? familyReason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<DuplicateChargeClaimAdminView>> RejectAsync(
            Guid claimId, Guid decidedByAccountId, string? decidedByAccountType,
            DateTime decidedOnUtc, string familyReason, string? decisionNote,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<DuplicateChargeClaimAdminView>> ExecuteAsync(
            Guid claimId, Guid? expectedExecutionAttemptId, Guid executedByAccountId,
            string? executedByAccountType, DateTime startedOnUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<DuplicateChargeClaimAdminView>> ReconcileAsync(
            DuplicateChargeClaimReconciliationInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
