using System.Reflection;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.UnitTests.Finance;

/// <summary>
/// W2-F0 money core: the per-family app credit balance (Owner decision #15 — a
/// real, non-withdrawable financial capability) and its non-negativity
/// invariant. Run green at the W2 integration gate by the Mastermind; domain
/// guards throw <see cref="DomainException"/> (house convention), which these
/// tests pin.
/// </summary>
public sealed class CreditBalanceTests
{
    private static readonly FamilyId Family = new(Guid.Parse("0198e2c2-6666-7777-8888-000000000201"));

    [Fact]
    public void Open_starts_at_zero_and_requires_a_family()
    {
        CreditBalance balance = CreditBalance.Open(Family);

        Assert.Equal(Family, balance.FamilyId);
        Assert.Equal(Money.Zero, balance.Balance);
        Assert.Equal(0L, balance.Balance.MinorUnits);
        Assert.Equal("EGP", balance.Currency);
        Assert.Equal(balance.CreatedOnUtc, balance.UpdatedOnUtc);

        Assert.Throws<DomainException>(() => CreditBalance.Open(FamilyId.Empty));
    }

    [Fact]
    public void Open_requires_a_utc_creation_timestamp()
    {
        Assert.Throws<DomainException>(() => CreditBalance.Open(
            Family,
            new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Post_increases_the_balance_exactly()
    {
        CreditBalance balance = CreditBalance.Open(Family);

        balance.Post(Money.FromMajorUnits(125.75m), Utc(1));

        Assert.Equal(12575L, balance.Balance.MinorUnits);
        Assert.Equal(Utc(1), balance.UpdatedOnUtc);
    }

    [Fact]
    public void Post_rejects_zero_and_negative_amounts_without_mutating()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(10m), Utc(1));

        Assert.Throws<DomainException>(() => balance.Post(Money.Zero, Utc(2)));
        Assert.Throws<DomainException>(() => balance.Post(Money.FromMinorUnits(-1), Utc(2)));

        Assert.Equal(1000L, balance.Balance.MinorUnits);
        Assert.Equal(Utc(1), balance.UpdatedOnUtc);
    }

    [Fact]
    public void Apply_to_invoice_reduces_the_balance_exactly()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(100m), Utc(1));

        balance.ApplyToInvoice(Money.FromMajorUnits(30.25m), Utc(2));

        Assert.Equal(6975L, balance.Balance.MinorUnits);
        Assert.Equal(Utc(2), balance.UpdatedOnUtc);
    }

    [Fact]
    public void Apply_to_invoice_can_drain_the_balance_exactly_to_zero()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(40m), Utc(1));

        balance.ApplyToInvoice(Money.FromMajorUnits(40m), Utc(2));
        Assert.Equal(0L, balance.Balance.MinorUnits);
        Assert.False(balance.Balance.IsNegative);
    }

    [Fact]
    public void Balance_can_never_go_negative()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(10m), Utc(1));

        Assert.Throws<DomainException>(() =>
            balance.ApplyToInvoice(Money.FromMajorUnits(10.01m), Utc(2)));

        Assert.Equal(1000L, balance.Balance.MinorUnits);
        Assert.Equal(Utc(1), balance.UpdatedOnUtc);
    }

    [Fact]
    public void Spending_from_an_empty_balance_is_refused()
    {
        CreditBalance balance = CreditBalance.Open(Family);

        Assert.Throws<DomainException>(() =>
            balance.ApplyToInvoice(Money.FromMajorUnits(1m), Utc(1)));

        Assert.Equal(0L, balance.Balance.MinorUnits);
    }

    [Fact]
    public void Refund_to_customer_unwinds_credit_with_the_same_non_negative_guard()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(50m), Utc(1));

        balance.RefundToCustomer(Money.FromMajorUnits(20m), Utc(2));
        Assert.Equal(3000L, balance.Balance.MinorUnits);

        Assert.Throws<DomainException>(() =>
            balance.RefundToCustomer(Money.FromMajorUnits(30.01m), Utc(3)));
        Assert.Equal(3000L, balance.Balance.MinorUnits);
    }

    [Fact]
    public void Zero_and_negative_operations_are_rejected_on_every_movement()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(5m), Utc(1));

        Assert.Throws<DomainException>(() => balance.ApplyToInvoice(Money.Zero, Utc(2)));
        Assert.Throws<DomainException>(() => balance.ApplyToInvoice(Money.FromMinorUnits(-5), Utc(2)));
        Assert.Throws<DomainException>(() => balance.RefundToCustomer(Money.Zero, Utc(2)));
        Assert.Throws<DomainException>(() => balance.RefundToCustomer(Money.FromMinorUnits(-5), Utc(2)));

        Assert.Equal(500L, balance.Balance.MinorUnits);
    }

    [Fact]
    public void Non_utc_movement_timestamps_are_rejected_without_mutation()
    {
        CreditBalance balance = CreditBalance.Open(Family);
        balance.Post(Money.FromMajorUnits(5m), Utc(1));

        DateTime local = new(2026, 10, 22, 1, 0, 0, DateTimeKind.Local);

        Assert.Throws<DomainException>(() => balance.Post(Money.FromMajorUnits(1m), local));
        Assert.Throws<DomainException>(() => balance.ApplyToInvoice(Money.FromMajorUnits(1m), local));
        Assert.Throws<DomainException>(() => balance.RefundToCustomer(Money.FromMajorUnits(1m), local));

        Assert.Equal(500L, balance.Balance.MinorUnits);
        Assert.Equal(Utc(1), balance.UpdatedOnUtc);
    }

    [Fact]
    public void Posted_credit_accumulates_across_multiple_awards()
    {
        CreditBalance balance = CreditBalance.Open(Family);

        balance.Post(Money.FromMajorUnits(33.33m), Utc(1));
        balance.Post(Money.FromMajorUnits(0.02m), Utc(2));
        balance.Post(Money.FromMajorUnits(66.65m), Utc(3));

        Assert.Equal(10000L, balance.Balance.MinorUnits);
    }

    [Fact]
    public void Balance_has_no_public_setter_so_money_only_moves_through_domain_operations()
    {
        Type type = typeof(CreditBalance);

        Assert.True(type.IsSealed);
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            property => Assert.Null(property.GetSetMethod(nonPublic: false)));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => method.Name),
            name => name is "Set" or "Update" or "Delete" or "Remove");
    }

    private static DateTime Utc(int hour) => new(2026, 10, 22, hour, 0, 0, DateTimeKind.Utc);
}
