using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Rentals.Application;
using Sanad.Modules.Rentals.Domain;
using Sanad.Modules.Rentals.Infrastructure.Persistence.Configurations;

namespace Sanad.Modules.Rentals.Infrastructure;

/// <summary>
/// The Rentals persistence context (Owner decision #33: "Rentals gets its own
/// migration slot"). Everything maps into the <c>rentals</c> schema; money
/// columns follow the standardized <c>numeric(18,2)</c> EGP convention
/// (Owner decision #37) while the domain keeps exact piastre integers. No
/// cross-DbContext foreign keys: correlation ids to other modules are plain
/// Guid columns (W3 design pin item 2).
/// </summary>
public sealed class RentalsDbContext(DbContextOptions<RentalsDbContext> options) : DbContext(options), IRentalsDbContext
{
    public const string Schema = "rentals";

    public DbSet<RentalEquipment> RentalEquipment => Set<RentalEquipment>();
    public DbSet<RentalBooking> RentalBookings => Set<RentalBooking>();
    public DbSet<RentalPaymentTransaction> RentalPaymentTransactions => Set<RentalPaymentTransaction>();
    public DbSet<RentalDamageDeduction> RentalDamageDeductions => Set<RentalDamageDeduction>();
    public DbSet<RentalRefundMovement> RentalRefundMovements => Set<RentalRefundMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new RentalEquipmentConfiguration());
        modelBuilder.ApplyConfiguration(new RentalBookingConfiguration());
        modelBuilder.ApplyConfiguration(new RentalPaymentTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new RentalDamageDeductionConfiguration());
        modelBuilder.ApplyConfiguration(new RentalRefundMovementConfiguration());
    }
}
