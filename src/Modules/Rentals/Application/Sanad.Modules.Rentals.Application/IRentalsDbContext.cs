using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Application;

/// <summary>
/// The persistence boundary of the Rentals module (mirrors
/// <c>IFinanceDbContext</c>/<c>ICareHomesDbContext</c>): handlers depend on
/// this interface; the concrete RentalsDbContext lives in
/// Rentals.Infrastructure and is bound by the module's DI registration.
/// </summary>
public interface IRentalsDbContext
{
    DbSet<RentalEquipment> RentalEquipment { get; }
    DbSet<RentalBooking> RentalBookings { get; }
    DbSet<RentalPaymentTransaction> RentalPaymentTransactions { get; }
    DbSet<RentalDamageDeduction> RentalDamageDeductions { get; }
    DbSet<RentalRefundMovement> RentalRefundMovements { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
