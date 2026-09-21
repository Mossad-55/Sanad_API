using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionRetirementCommandTests
{
    [Fact]
    public async Task Retires_published_plan_once_and_records_server_actor_audit()
    {
        await using var db = CreateContext();
        var actor = UserId.New();
        var plan = PublishedPlan();
        db.SubscriptionPlanVersions.Add(plan);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new(plan.Id, actor), default);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(db.SubscriptionPlanRetirementAudits);
        Assert.Equal(actor, audit.ActorUserId);
        Assert.Equal("SuperAdmin", audit.ActorRole);
        Assert.Equal(plan.Id, audit.PlanVersionId);
        Assert.Equal("premium", audit.PlanKey);
        Assert.Equal(1, audit.PlanVersion);
        Assert.True(audit.OldAvailability);
        Assert.False(audit.NewAvailability);
        Assert.Equal(DateTimeKind.Utc, audit.RetiredOnUtc.Kind);
        Assert.False(plan.IsAvailableForNewSales);
    }

    [Fact]
    public async Task Missing_unpublished_and_already_retired_plans_return_expected_errors_without_audit()
    {
        await using var db = CreateContext();
        var published = PublishedPlan();
        var draft = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        var retired = PublishedPlan(available: false);
        db.SubscriptionPlanVersions.AddRange(published, draft, retired);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        Assert.Equal("Subscriptions.Plan.NotFound", (await handler.Handle(new(Guid.NewGuid(), UserId.New()), default)).Error.Code);
        Assert.Equal("Subscriptions.Plan.NotPublished", (await handler.Handle(new(draft.Id, UserId.New()), default)).Error.Code);
        Assert.Equal("Subscriptions.Plan.AlreadyRetired", (await handler.Handle(new(retired.Id, UserId.New()), default)).Error.Code);
        Assert.Empty(db.SubscriptionPlanRetirementAudits);
    }

    [Fact]
    public async Task Save_failure_does_not_report_success_or_append_audit()
    {
        await using var inner = CreateContext();
        var plan = PublishedPlan();
        inner.SubscriptionPlanVersions.Add(plan);
        await inner.SaveChangesAsync();
        await using var db = new ThrowingSaveContext(inner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new(plan.Id, UserId.New()), default));

        Assert.Equal("save failed", exception.Message);
        Assert.Empty(inner.SubscriptionPlanRetirementAudits);
        inner.ChangeTracker.Clear();
        Assert.True(inner.SubscriptionPlanVersions.Single().IsAvailableForNewSales);
    }

    [Fact]
    public async Task Concurrency_conflict_maps_to_already_retired_without_duplicate_audit()
    {
        await using var inner = CreateContext();
        var plan = PublishedPlan();
        inner.SubscriptionPlanVersions.Add(plan);
        await inner.SaveChangesAsync();
        await using var db = new ConcurrencyContext(inner);

        var result = await Handler(db).Handle(new(plan.Id, UserId.New()), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Plan.AlreadyRetired", result.Error.Code);
        Assert.Empty(inner.SubscriptionPlanRetirementAudits);
        inner.ChangeTracker.Clear();
        Assert.True(inner.SubscriptionPlanVersions.Single().IsAvailableForNewSales);
    }

    private static RetireSubscriptionPlanCommandHandler Handler(IFamiliesDbContext db) =>
        new(db);

    private static SubscriptionPlanVersion PublishedPlan(bool available = true) =>
        SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            isPublished: true,
            isAvailableForNewSales: available,
            createdOnUtc: DateTime.UtcNow,
            publishedOnUtc: DateTime.UtcNow);

    private static FamiliesDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private class ForwardingContext(FamiliesDbContext inner) : IFamiliesDbContext, IAsyncDisposable
    {
        protected FamiliesDbContext Inner { get; } = inner;
        public DbSet<Family> Families => Inner.Families;
        public DbSet<Elderly> Elderlies => Inner.Elderlies;
        public DbSet<FamilyInvitation> Invitations => Inner.Invitations;
        public DbSet<Booking> Bookings => Inner.Bookings;
        public DbSet<BookingCancellationFact> BookingCancellationFacts => Inner.BookingCancellationFacts;
        public DbSet<AssessmentQuestion> AssessmentQuestions => Inner.AssessmentQuestions;
        public DbSet<AssessmentTier> AssessmentTiers => Inner.AssessmentTiers;
        public DbSet<CareAssessment> CareAssessments => Inner.CareAssessments;
        public DbSet<Medication> Medications => Inner.Medications;
        public DbSet<MedicationDoseLog> MedicationDoseLogs => Inner.MedicationDoseLogs;
        public DbSet<ElderlyNote> ElderlyNotes => Inner.ElderlyNotes;
        public DbSet<ElderlyActivityLog> ElderlyActivityLogs => Inner.ElderlyActivityLogs;
        public DbSet<VisitReport> VisitReports => Inner.VisitReports;
        public DbSet<MedicalReport> MedicalReports => Inner.MedicalReports;
        public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => Inner.SubscriptionPlanVersions;
        public DbSet<FamilySubscription> FamilySubscriptions => Inner.FamilySubscriptions;
        public DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits => Inner.SubscriptionPlanRetirementAudits;
        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Inner.SaveChangesAsync(cancellationToken);
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }

    private sealed class ThrowingSaveContext(FamiliesDbContext inner) : ForwardingContext(inner)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("save failed");
    }

    private sealed class ConcurrencyContext(FamiliesDbContext inner) : ForwardingContext(inner)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException("concurrency conflict");
    }
}
