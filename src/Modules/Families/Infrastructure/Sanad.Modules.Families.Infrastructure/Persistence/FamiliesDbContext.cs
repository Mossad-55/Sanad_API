using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;

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
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();
    public DbSet<AssessmentTier> AssessmentTiers => Set<AssessmentTier>();
    public DbSet<CareAssessment> CareAssessments => Set<CareAssessment>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<MedicationDoseLog> MedicationDoseLogs => Set<MedicationDoseLog>();
    public DbSet<ElderlyNote> ElderlyNotes => Set<ElderlyNote>();
    public DbSet<ElderlyActivityLog> ElderlyActivityLogs => Set<ElderlyActivityLog>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FamiliesDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}