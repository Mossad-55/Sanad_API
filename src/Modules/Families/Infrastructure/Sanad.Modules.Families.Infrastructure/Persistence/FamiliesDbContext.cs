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
}