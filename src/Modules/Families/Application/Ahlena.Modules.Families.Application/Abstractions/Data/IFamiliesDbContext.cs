using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Families.Domain.Activities;
using Ahlena.Modules.Families.Domain.Assessments;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Elderlies.CheckIns;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Invitations;
using Ahlena.Modules.Families.Domain.Medications;
using Ahlena.Modules.Families.Domain.Notes;
using Ahlena.Modules.Families.Domain.Reports;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Domain.HelpRequests;
using Ahlena.Modules.Families.Domain.Idempotency;
using Ahlena.Modules.Families.Domain.Sos;

namespace Ahlena.Modules.Families.Application.Abstractions.Data;

public interface IFamiliesDbContext
{
    DbSet<Family> Families { get; }
    DbSet<Elderly> Elderlies { get; }
    DbSet<ElderlyCheckIn> ElderlyCheckIns { get; }
    DbSet<FamilyInvitation> Invitations { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingCancellationFact> BookingCancellationFacts { get; }
    DbSet<AssessmentQuestion> AssessmentQuestions { get; }
    DbSet<AssessmentTier> AssessmentTiers { get; }
    DbSet<CareAssessment> CareAssessments { get; }
    DbSet<Medication> Medications { get; }
    DbSet<MedicationDoseLog> MedicationDoseLogs { get; }
    DbSet<AdminMedicationAccessAudit> AdminMedicationAccessAudits { get; }
    DbSet<ElderlyNote> ElderlyNotes { get; }
    DbSet<ElderlyActivityLog> ElderlyActivityLogs { get; }
    DbSet<VisitReport> VisitReports { get; }
    DbSet<MedicalReport> MedicalReports { get; }
    DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions { get; }
    DbSet<FamilySubscription> FamilySubscriptions { get; }
    DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits { get; }
    DbSet<SubscriptionCoupon> SubscriptionCoupons { get; }
    DbSet<SubscriptionTaxRule> SubscriptionTaxRules { get; }
    DbSet<SubscriptionPaymentAttempt> SubscriptionPaymentAttempts { get; }
    DbSet<PaymobSubscriptionCallback> PaymobSubscriptionCallbacks { get; }
    DbSet<PaymobSubscriptionIdentity> PaymobSubscriptionIdentities { get; }
    DbSet<SubscriptionInvoice> SubscriptionInvoices { get; }
    DbSet<ElderlyHelpRequest> ElderlyHelpRequests { get; }
    DbSet<ElderlyHelpRequestHistory> ElderlyHelpRequestHistories { get; }
    DbSet<ElderlySos> ElderlySos { get; }
    DbSet<ElderlySosHistory> ElderlySosHistories { get; }

    DbSet<BookingReview> BookingReviews { get; }
    DbSet<MedicalAccessGrant> MedicalAccessGrants { get; }
    DbSet<CaregiverMedicalAccessAudit> CaregiverMedicalAccessAudits { get; }

    /// <summary>Three-source care records (W1 care-records slice).</summary>
    DbSet<CareRecord> CareRecords { get; }

    /// <summary>Append-only, payload-free care-record audit trail.</summary>
    DbSet<CareRecordAudit> CareRecordAudits { get; }

    /// <summary>
    /// Accepted idempotent create attempts. The unique
    /// <c>(actor_id, operation, idempotency_key)</c> index makes concurrent
    /// duplicates execute exactly once.
    /// </summary>
    DbSet<IdempotencyRequest> IdempotencyRequests { get; }

    /// <summary>
    /// Queues the provider identity claim in the same unit of work as the callback mutation.
    /// The shared unique registry is the authoritative race boundary; the claim is not accepted
    /// until the subsequent SaveChangesAsync transaction succeeds.
    /// </summary>
    void ReservePaymobSubscriptionIdentity(PaymobSubscriptionIdentity identity) =>
        PaymobSubscriptionIdentities.Add(identity);

    /// <summary>
    /// Detaches every pending <see cref="EntityState.Added"/> entity of
    /// <typeparamref name="TEntity"/>. Used by the idempotency race path: the
    /// loser's rows never reached the database and must not be retried.
    /// </summary>
    void DetachAdded<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
