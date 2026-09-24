using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Application.Abstractions.Data;
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

namespace Sanad.Modules.Families.Infrastructure.Persistence;

public sealed class FamiliesDbContext :
    DbContext,
    IFamiliesDbContext
{
    public const string Schema = "families";

    public FamiliesDbContext(
        DbContextOptions<FamiliesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Family> Families => Set<Family>();
    public DbSet<Elderly> Elderlies => Set<Elderly>();
    public DbSet<FamilyInvitation> Invitations => Set<FamilyInvitation>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingCancellationFact> BookingCancellationFacts => Set<BookingCancellationFact>();
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();
    public DbSet<AssessmentTier> AssessmentTiers => Set<AssessmentTier>();
    public DbSet<CareAssessment> CareAssessments => Set<CareAssessment>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<MedicationDoseLog> MedicationDoseLogs => Set<MedicationDoseLog>();
    public DbSet<ElderlyNote> ElderlyNotes => Set<ElderlyNote>();
    public DbSet<ElderlyActivityLog> ElderlyActivityLogs => Set<ElderlyActivityLog>();
    public DbSet<VisitReport> VisitReports => Set<VisitReport>();
    public DbSet<MedicalReport> MedicalReports => Set<MedicalReport>();
    public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => Set<SubscriptionPlanVersion>();
    public DbSet<FamilySubscription> FamilySubscriptions => Set<FamilySubscription>();
    public DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits => Set<SubscriptionPlanRetirementAudit>();
    public DbSet<SubscriptionCoupon> SubscriptionCoupons => Set<SubscriptionCoupon>();
    public DbSet<SubscriptionTaxRule> SubscriptionTaxRules => Set<SubscriptionTaxRule>();
    public DbSet<SubscriptionPaymentAttempt> SubscriptionPaymentAttempts => Set<SubscriptionPaymentAttempt>();
    public DbSet<PaymobSubscriptionCallback> PaymobSubscriptionCallbacks => Set<PaymobSubscriptionCallback>();
    public DbSet<PaymobSubscriptionIdentity> PaymobSubscriptionIdentities => Set<PaymobSubscriptionIdentity>();

    public void ReservePaymobSubscriptionIdentity(PaymobSubscriptionIdentity identity) =>
        PaymobSubscriptionIdentities.Add(identity);

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FamiliesDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
        => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ThrowIfCancellationFactMutated();
        ThrowIfVisitReportMutated();
        ThrowIfMedicalReportMutated();
        ThrowIfSubscriptionPlanVersionMutated();
        ThrowIfSubscriptionPlanRetirementAuditMutated();
        ThrowIfFamilySubscriptionMutated();
        ThrowIfSubscriptionCouponMutated();
        ThrowIfSubscriptionTaxRuleMutated();
        ThrowIfPaymobSubscriptionCallbackMutated();
        ThrowIfPaymobSubscriptionIdentityMutated();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
        => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ThrowIfCancellationFactMutated();
        ThrowIfVisitReportMutated();
        ThrowIfMedicalReportMutated();
        ThrowIfSubscriptionPlanVersionMutated();
        ThrowIfSubscriptionPlanRetirementAuditMutated();
        ThrowIfFamilySubscriptionMutated();
        ThrowIfSubscriptionCouponMutated();
        ThrowIfSubscriptionTaxRuleMutated();
        ThrowIfPaymobSubscriptionCallbackMutated();
        ThrowIfPaymobSubscriptionIdentityMutated();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Enforces that <see cref="BookingCancellationFact"/> stays append-only history: a tracked fact
    /// in the <see cref="EntityState.Modified"/> or <see cref="EntityState.Deleted"/> state fails the
    /// save before anything reaches the database.
    /// <para>
    /// Known limitation: the guard inspects only changes tracked by this context instance. Raw SQL
    /// (<c>ExecuteSqlRaw</c>/<c>ExecuteSql</c>), bulk operations, migrations, and other
    /// <see cref="DbContext"/> instances bypass it entirely.
    /// </para>
    /// </summary>
    private void ThrowIfCancellationFactMutated()
    {
        foreach (var entry in ChangeTracker.Entries<BookingCancellationFact>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(BookingCancellationFact)} entries are immutable, append-only history: " +
                    "they must never be updated or deleted, but a tracked fact with " +
                    $"Id '{entry.Entity.Id}' is in state '{entry.State}'. " +
                    "Discard the mutation instead of saving it.");
            }
        }
    }

    /// <summary>
    /// Enforces that <see cref="VisitReport"/> remains immutable after its single append. A report
    /// can be created once for a booking, but an existing report cannot be edited or deleted.
    /// </summary>
    private void ThrowIfVisitReportMutated()
    {
        foreach (var entry in ChangeTracker.Entries<VisitReport>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(VisitReport)} entries are immutable: " +
                    "they must never be updated or deleted, but a tracked report with " +
                    $"Id '{entry.Entity.Id}' is in state '{entry.State}'.");
            }
        }
    }

    private void ThrowIfMedicalReportMutated()
    {
        foreach (var entry in ChangeTracker.Entries<MedicalReport>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{nameof(MedicalReport)} entries are immutable and cannot be updated or deleted.");
        }
    }

    private void ThrowIfSubscriptionPlanVersionMutated()
    {
        foreach (var entry in ChangeTracker.Entries<SubscriptionPlanVersion>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(SubscriptionPlanVersion)} entries are immutable and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.IsModified && property.Metadata.Name is not
                        (nameof(SubscriptionPlanVersion.IsAvailableForNewSales)
                        or nameof(SubscriptionPlanVersion.IsPublished)
                        or nameof(SubscriptionPlanVersion.PublishedOnUtc))))
            {
                throw new InvalidOperationException(
                    $"{nameof(SubscriptionPlanVersion)} catalog fields are immutable; only " +
                    $"{nameof(SubscriptionPlanVersion.IsAvailableForNewSales)}, " +
                    $"{nameof(SubscriptionPlanVersion.IsPublished)}, and " +
                    $"{nameof(SubscriptionPlanVersion.PublishedOnUtc)} may be changed.");
            }
        }
    }

    private void ThrowIfFamilySubscriptionMutated()
    {
        foreach (var entry in ChangeTracker.Entries<FamilySubscription>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(FamilySubscription)} entries are immutable and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.IsModified && property.Metadata.Name is not
                        (nameof(FamilySubscription.IsCurrent)
                        or nameof(FamilySubscription.AutoRenewEnabled)
                        or nameof(FamilySubscription.CancellationRequestedOnUtc)
                        or nameof(FamilySubscription.CurrentPeriodEndsOnUtc)
                        or nameof(FamilySubscription.RenewalGraceEndsOnUtc)
                        or nameof(FamilySubscription.LastRenewalFailedOnUtc)
                        or nameof(FamilySubscription.PaymobSubscriptionId)
                        or nameof(FamilySubscription.PaymobSubscriptionState)
                        or nameof(FamilySubscription.PaymobNextBillingOnUtc)
                        or nameof(FamilySubscription.PaymobLastCallbackKey)
                        or nameof(FamilySubscription.LifecycleVersion))))
            {
                throw new InvalidOperationException(
                    $"{nameof(FamilySubscription)} snapshot fields are immutable; only lifecycle fields " +
                    $"{nameof(FamilySubscription.IsCurrent)}, {nameof(FamilySubscription.AutoRenewEnabled)}, " +
                    $"{nameof(FamilySubscription.CancellationRequestedOnUtc)}, {nameof(FamilySubscription.CurrentPeriodEndsOnUtc)}, " +
                    $"{nameof(FamilySubscription.RenewalGraceEndsOnUtc)}, " +
                    $"{nameof(FamilySubscription.LastRenewalFailedOnUtc)}, " +
                    $"provider identity/state ({nameof(FamilySubscription.PaymobSubscriptionId)}, " +
                    $"{nameof(FamilySubscription.PaymobSubscriptionState)}, " +
                    $"{nameof(FamilySubscription.PaymobNextBillingOnUtc)}, " +
                    $"{nameof(FamilySubscription.PaymobLastCallbackKey)}), and " +
                    $"{nameof(FamilySubscription.LifecycleVersion)} may be changed.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<SubscriptionBenefit>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(SubscriptionBenefit)} entries in subscription snapshots are immutable.");
            }
        }
    }

    private void ThrowIfSubscriptionPlanRetirementAuditMutated()
    {
        foreach (var entry in ChangeTracker.Entries<SubscriptionPlanRetirementAudit>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    $"{nameof(SubscriptionPlanRetirementAudit)} entries are immutable and cannot be updated or deleted.");
        }
    }

    private void ThrowIfSubscriptionCouponMutated()
    {
        foreach (var entry in ChangeTracker.Entries<SubscriptionCoupon>())
        {
            if (entry.State == EntityState.Modified)
                throw new InvalidOperationException($"{nameof(SubscriptionCoupon)} entries are immutable and cannot be updated.");
        }
    }

    private void ThrowIfSubscriptionTaxRuleMutated()
    {
        foreach (var entry in ChangeTracker.Entries<SubscriptionTaxRule>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(SubscriptionTaxRule)} entries are immutable and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.IsModified && property.Metadata.Name is not nameof(SubscriptionTaxRule.IsActive)))
            {
                throw new InvalidOperationException(
                    $"{nameof(SubscriptionTaxRule)} versioned fields are immutable; only " +
                    $"{nameof(SubscriptionTaxRule.IsActive)} may be changed.");
            }
        }
    }

    private void ThrowIfPaymobSubscriptionCallbackMutated()
    {
        foreach (var entry in ChangeTracker.Entries<PaymobSubscriptionCallback>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    $"{nameof(PaymobSubscriptionCallback)} entries are immutable and cannot be updated or deleted.");
        }
    }

    private void ThrowIfPaymobSubscriptionIdentityMutated()
    {
        foreach (var entry in ChangeTracker.Entries<PaymobSubscriptionIdentity>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    $"{nameof(PaymobSubscriptionIdentity)} entries are immutable and cannot be updated or deleted.");
        }
    }
}
