using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Activities;
using Ahlena.Modules.Families.Domain.Assessments;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Elderlies.CheckIns;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.HelpRequests;
using Ahlena.Modules.Families.Domain.Idempotency;
using Ahlena.Modules.Families.Domain.Invitations;
using Ahlena.Modules.Families.Domain.Medications;
using Ahlena.Modules.Families.Domain.Notes;
using Ahlena.Modules.Families.Domain.Reports;
using Ahlena.Modules.Families.Domain.Sos;
using Ahlena.Modules.Families.Domain.Subscriptions;

namespace Ahlena.UnitTests.Support;

internal abstract class FamiliesDbContextAdapter(IFamiliesDbContext inner)
    : IFamiliesDbContext
{
    protected IFamiliesDbContext Inner { get; } = inner;

    public DbSet<Family> Families => Inner.Families;
    public DbSet<Elderly> Elderlies => Inner.Elderlies;
    public DbSet<ElderlyCheckIn> ElderlyCheckIns => Inner.ElderlyCheckIns;
    public DbSet<FamilyInvitation> Invitations => Inner.Invitations;
    public DbSet<Booking> Bookings => Inner.Bookings;
    public DbSet<BookingCancellationFact> BookingCancellationFacts => Inner.BookingCancellationFacts;
    public DbSet<BookingReview> BookingReviews => Inner.BookingReviews;
    public DbSet<CareRecord> CareRecords => Inner.CareRecords;
    public DbSet<CareRecordAudit> CareRecordAudits => Inner.CareRecordAudits;
    public DbSet<MedicalAccessGrant> MedicalAccessGrants => Inner.MedicalAccessGrants;
    public DbSet<CaregiverMedicalAccessAudit> CaregiverMedicalAccessAudits => Inner.CaregiverMedicalAccessAudits;
    public DbSet<AssessmentQuestion> AssessmentQuestions => Inner.AssessmentQuestions;
    public DbSet<AssessmentTier> AssessmentTiers => Inner.AssessmentTiers;
    public DbSet<CareAssessment> CareAssessments => Inner.CareAssessments;
    public DbSet<Medication> Medications => Inner.Medications;
    public DbSet<MedicationDoseLog> MedicationDoseLogs => Inner.MedicationDoseLogs;
    public DbSet<AdminMedicationAccessAudit> AdminMedicationAccessAudits => Inner.AdminMedicationAccessAudits;
    public DbSet<ElderlyNote> ElderlyNotes => Inner.ElderlyNotes;
    public DbSet<ElderlyActivityLog> ElderlyActivityLogs => Inner.ElderlyActivityLogs;
    public DbSet<VisitReport> VisitReports => Inner.VisitReports;
    public DbSet<MedicalReport> MedicalReports => Inner.MedicalReports;
    public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => Inner.SubscriptionPlanVersions;
    public DbSet<FamilySubscription> FamilySubscriptions => Inner.FamilySubscriptions;
    public DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits => Inner.SubscriptionPlanRetirementAudits;
    public DbSet<SubscriptionCoupon> SubscriptionCoupons => Inner.SubscriptionCoupons;
    public DbSet<SubscriptionTaxRule> SubscriptionTaxRules => Inner.SubscriptionTaxRules;
    public DbSet<SubscriptionPaymentAttempt> SubscriptionPaymentAttempts => Inner.SubscriptionPaymentAttempts;
    public DbSet<PaymobSubscriptionCallback> PaymobSubscriptionCallbacks => Inner.PaymobSubscriptionCallbacks;
    public DbSet<PaymobSubscriptionIdentity> PaymobSubscriptionIdentities => Inner.PaymobSubscriptionIdentities;
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Inner.SubscriptionInvoices;
    public DbSet<ElderlyHelpRequest> ElderlyHelpRequests => Inner.ElderlyHelpRequests;
    public DbSet<ElderlyHelpRequestHistory> ElderlyHelpRequestHistories => Inner.ElderlyHelpRequestHistories;
    public DbSet<ElderlySos> ElderlySos => Inner.ElderlySos;
    public DbSet<ElderlySosHistory> ElderlySosHistories => Inner.ElderlySosHistories;
    public DbSet<IdempotencyRequest> IdempotencyRequests => Inner.IdempotencyRequests;

    public void DetachAdded<TEntity>() where TEntity : class =>
        Inner.DetachAdded<TEntity>();

    public void ReservePaymobSubscriptionIdentity(PaymobSubscriptionIdentity identity) =>
        Inner.ReservePaymobSubscriptionIdentity(identity);

    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Inner.SaveChangesAsync(cancellationToken);
}
