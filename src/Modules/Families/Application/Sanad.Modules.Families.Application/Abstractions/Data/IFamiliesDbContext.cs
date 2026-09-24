using Microsoft.EntityFrameworkCore;
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

namespace Sanad.Modules.Families.Application.Abstractions.Data;

public interface IFamiliesDbContext
{
    DbSet<Family> Families { get; }
    DbSet<Elderly> Elderlies { get; }
    DbSet<FamilyInvitation> Invitations { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingCancellationFact> BookingCancellationFacts { get; }
    DbSet<AssessmentQuestion> AssessmentQuestions { get; }
    DbSet<AssessmentTier> AssessmentTiers { get; }
    DbSet<CareAssessment> CareAssessments { get; }
    DbSet<Medication> Medications { get; }
    DbSet<MedicationDoseLog> MedicationDoseLogs { get; }
    DbSet<ElderlyNote> ElderlyNotes { get; }
    DbSet<ElderlyActivityLog> ElderlyActivityLogs { get; }
    DbSet<VisitReport> VisitReports { get; }
    DbSet<MedicalReport> MedicalReports => throw new NotSupportedException("This context does not expose medical reports.");
    DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => throw new NotSupportedException("This context does not expose subscription plans.");
    DbSet<FamilySubscription> FamilySubscriptions => throw new NotSupportedException("This context does not expose family subscriptions.");
    DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits => throw new NotSupportedException("This context does not expose subscription plan retirement audits.");
    DbSet<SubscriptionCoupon> SubscriptionCoupons => throw new NotSupportedException("This context does not expose subscription coupons.");
    DbSet<SubscriptionTaxRule> SubscriptionTaxRules => throw new NotSupportedException("This context does not expose subscription tax rules.");
    DbSet<SubscriptionPaymentAttempt> SubscriptionPaymentAttempts => throw new NotSupportedException("This context does not expose subscription payment attempts.");
    DbSet<PaymobSubscriptionCallback> PaymobSubscriptionCallbacks => throw new NotSupportedException("This context does not expose Paymob subscription callbacks.");
    DbSet<PaymobSubscriptionIdentity> PaymobSubscriptionIdentities => throw new NotSupportedException("This context does not expose Paymob subscription identities.");

    /// <summary>
    /// Queues the provider identity claim in the same unit of work as the callback mutation.
    /// The shared unique registry is the authoritative race boundary; the claim is not accepted
    /// until the subsequent SaveChangesAsync transaction succeeds.
    /// </summary>
    void ReservePaymobSubscriptionIdentity(PaymobSubscriptionIdentity identity) =>
        PaymobSubscriptionIdentities.Add(identity);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
