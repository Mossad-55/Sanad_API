using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Domain.Sos;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.Support;

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
    public DbSet<MedicalAccessGrant> MedicalAccessGrants => Inner.MedicalAccessGrants;
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

    public void ReservePaymobSubscriptionIdentity(PaymobSubscriptionIdentity identity) =>
        Inner.ReservePaymobSubscriptionIdentity(identity);

    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Inner.SaveChangesAsync(cancellationToken);
}
