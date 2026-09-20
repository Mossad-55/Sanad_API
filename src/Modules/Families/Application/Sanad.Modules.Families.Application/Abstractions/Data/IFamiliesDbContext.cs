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

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
