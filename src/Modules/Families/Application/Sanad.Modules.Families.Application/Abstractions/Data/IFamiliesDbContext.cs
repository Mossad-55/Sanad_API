using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Application.Abstractions.Data;

public interface IFamiliesDbContext
{
    DbSet<Family> Families { get; }
    DbSet<Elderly> Elderlies { get; }
    DbSet<FamilyInvitation> Invitations { get; }
    DbSet<AssessmentQuestion> AssessmentQuestions { get; }
    DbSet<AssessmentTier> AssessmentTiers { get; }
    DbSet<CareAssessment> CareAssessments { get; }
    DbSet<Medication> Medications { get; }
    DbSet<MedicationDoseLog> MedicationDoseLogs { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}