using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
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

public sealed class SubscriptionCancellationCommandTests
{
    [Fact]
    public async Task Owner_can_cancel_once_and_repeat_is_conflict_without_provider_behavior()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var family = Family.Create(owner);
        var plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan));
        await db.SaveChangesAsync();

        var handler = new CancelSubscriptionRenewalCommandHandler(db);
        var first = await handler.Handle(new(owner), default);
        var second = await handler.Handle(new(owner), default);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("Subscriptions.CancelRenewal.AlreadyRequested", second.Error.Code);
        Assert.False(db.FamilySubscriptions.Single().AutoRenewEnabled);
        Assert.NotNull(db.FamilySubscriptions.Single().CancellationRequestedOnUtc);
    }

    [Fact]
    public async Task Editor_and_viewer_cannot_cancel_or_reenable()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var editor = UserId.New();
        var viewer = UserId.New();
        var family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));
        family.AddMember(FamilyMember.Create(viewer, owner, FamilyRelationshipType.Other, FamilyRole.Viewer));
        var plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan));
        await db.SaveChangesAsync();

        foreach (var user in new[] { editor, viewer })
        {
            var cancel = await new CancelSubscriptionRenewalCommandHandler(db).Handle(new(user), default);
            var reenable = await new ReenableSubscriptionAutoRenewCommandHandler(db).Handle(new(user), default);
            Assert.False(cancel.IsSuccess);
            Assert.False(reenable.IsSuccess);
            Assert.Equal("Families.Family.NotOwner", cancel.Error.Code);
            Assert.Equal("Families.Family.NotOwner", reenable.Error.Code);
        }
    }

    [Fact]
    public async Task Reenable_succeeds_before_end_and_is_conflict_at_or_after_end()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var family = Family.Create(owner);
        var plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan, DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        await db.SaveChangesAsync();

        var subscription = db.FamilySubscriptions.Single();
        subscription.CancelRenewal(DateTime.UtcNow);
        await db.SaveChangesAsync();
        var result = await new ReenableSubscriptionAutoRenewCommandHandler(db).Handle(new(owner), default);
        Assert.True(result.IsSuccess);

        subscription.CancelRenewal(DateTime.UtcNow);
        typeof(FamilySubscription).GetProperty(nameof(FamilySubscription.CurrentPeriodEndsOnUtc))!
            .SetValue(subscription, DateTime.UtcNow.AddTicks(-1));
        result = await new ReenableSubscriptionAutoRenewCommandHandler(db).Handle(new(owner), default);
        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.ReenableAutoRenew.PeriodEnded", result.Error.Code);
    }

    [Fact]
    public async Task Stale_cancel_save_maps_to_already_requested()
    {
        await using var inner = CreateContext();
        var owner = UserId.New();
        var family = Family.Create(owner);
        var plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        inner.Families.Add(family);
        inner.SubscriptionPlanVersions.Add(plan);
        inner.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan));
        await inner.SaveChangesAsync();

        await using var db = new ConcurrencyContext(inner);
        var result = await new CancelSubscriptionRenewalCommandHandler(db).Handle(new(owner), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.CancelRenewal.AlreadyRequested", result.Error.Code);
    }

    [Fact]
    public async Task Stale_reenable_save_maps_to_already_requested()
    {
        await using var inner = CreateContext();
        var owner = UserId.New();
        var family = Family.Create(owner);
        var plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        var subscription = FamilySubscription.Create(family.Id, plan);
        inner.Families.Add(family);
        inner.SubscriptionPlanVersions.Add(plan);
        inner.FamilySubscriptions.Add(subscription);
        await inner.SaveChangesAsync();
        subscription.CancelRenewal(DateTime.UtcNow);
        await inner.SaveChangesAsync();

        await using var db = new ConcurrencyContext(inner);
        var result = await new ReenableSubscriptionAutoRenewCommandHandler(db).Handle(new(owner), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.CancelRenewal.AlreadyRequested", result.Error.Code);
    }

    private sealed class ConcurrencyContext(FamiliesDbContext inner) : IFamiliesDbContext, IAsyncDisposable
    {
        public DbSet<Family> Families => inner.Families;
        public DbSet<Elderly> Elderlies => inner.Elderlies;
        public DbSet<FamilyInvitation> Invitations => inner.Invitations;
        public DbSet<Booking> Bookings => inner.Bookings;
        public DbSet<BookingCancellationFact> BookingCancellationFacts => inner.BookingCancellationFacts;
        public DbSet<AssessmentQuestion> AssessmentQuestions => inner.AssessmentQuestions;
        public DbSet<AssessmentTier> AssessmentTiers => inner.AssessmentTiers;
        public DbSet<CareAssessment> CareAssessments => inner.CareAssessments;
        public DbSet<Medication> Medications => inner.Medications;
        public DbSet<MedicationDoseLog> MedicationDoseLogs => inner.MedicationDoseLogs;
        public DbSet<ElderlyNote> ElderlyNotes => inner.ElderlyNotes;
        public DbSet<ElderlyActivityLog> ElderlyActivityLogs => inner.ElderlyActivityLogs;
        public DbSet<VisitReport> VisitReports => inner.VisitReports;
        public DbSet<MedicalReport> MedicalReports => inner.MedicalReports;
        public DbSet<FamilySubscription> FamilySubscriptions => inner.FamilySubscriptions;
        public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => inner.SubscriptionPlanVersions;
        public DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits => inner.SubscriptionPlanRetirementAudits;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new DbUpdateConcurrencyException();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static FamiliesDbContext CreateContext() => new(new DbContextOptionsBuilder<FamiliesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
