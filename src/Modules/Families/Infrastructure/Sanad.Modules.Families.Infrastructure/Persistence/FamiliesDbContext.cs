using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;

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

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FamiliesDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}