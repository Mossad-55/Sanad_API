using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// App credit balance for one family account.
/// <para>
/// Owner decision #15 makes explicitly chosen app credit a real, recorded,
/// non-withdrawable financial balance usable for eligible Ahlena purchases —
/// not merely a refund label. Two rules follow from that and are enforced here
/// rather than by callers:
/// </para>
/// <list type="bullet">
/// <item>Money only moves through the explicit domain operations
/// <see cref="Post"/>, <see cref="ApplyToInvoice"/> and
/// <see cref="RefundToCustomer"/>. There is no public setter, so no caller can
/// assign a balance directly.</item>
/// <item>The balance can never go negative. Any operation that would take it
/// below zero fails and leaves the balance untouched.</item>
/// </list>
/// <para>
/// Balances are stored in piastres as the signed major-unit column
/// <c>balance</c> (<c>numeric(18,2)</c> EGP per Owner decision #37, matching the rest of the
/// codebase). Credit is non-withdrawable: <see cref="RefundToCustomer"/>
/// exists only to unwind a credit when a refund is completed through the
/// original payment method instead, and still cannot drive the balance
/// negative.
/// </para>
/// </summary>
public sealed class CreditBalance : AggregateRoot<Guid>
{
    public const string DefaultCurrency = Money.CurrencyCode;

    private CreditBalance() { }

    private CreditBalance(Guid id, FamilyId familyId, Money balance, DateTime createdOnUtc) : base(id)
    {
        FamilyId = familyId;
        Balance = balance;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public FamilyId FamilyId { get; private set; }

    /// <summary>Current balance. Never negative by construction.</summary>
    public Money Balance { get; private set; } = Money.Zero;

    public string Currency => DefaultCurrency;

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static CreditBalance Open(FamilyId familyId, DateTime? createdOnUtc = null)
    {
        if (familyId == FamilyId.Empty)
            throw new DomainException("An app credit balance requires a family account.");

        DateTime created = createdOnUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new DomainException("An app credit balance timestamp must be UTC.");

        return new CreditBalance(Guid.CreateVersion7(), familyId, Money.Zero, created);
    }

    /// <summary>
    /// Posts an approved award into the credit balance (#15). The amount must be
    /// strictly positive: a zero-piastre proration result is a real outcome
    /// (#31), but there is nothing to post, so callers must not create a
    /// meaningless zero balance movement.
    /// </summary>
    public Money Post(Money amount, DateTime occurredOnUtc)
    {
        if (amount.IsNegative || amount.IsZero)
            throw new DomainException("A credit balance movement requires a positive amount.");

        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("An app credit balance timestamp must be UTC.");

        Balance = Balance.Add(amount);
        UpdatedOnUtc = occurredOnUtc;
        return Balance;
    }

    /// <summary>
    /// Applies credit to an eligible Ahlena purchase. Fails, and changes
    /// nothing, when the balance is insufficient — the balance is never allowed
    /// to go negative.
    /// </summary>
    public Money ApplyToInvoice(Money amount, DateTime occurredOnUtc)
    {
        if (amount.IsNegative || amount.IsZero)
            throw new DomainException("A credit balance movement requires a positive amount.");

        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("An app credit balance timestamp must be UTC.");

        if (amount > Balance)
            throw new DomainException("The app credit balance is lower than the requested amount.");

        Balance = Balance.Subtract(amount);
        UpdatedOnUtc = occurredOnUtc;
        return Balance;
    }

    /// <summary>
    /// Unwinds credit that was posted for an award later completed through the
    /// original payment method. Same non-negative guard as
    /// <see cref="ApplyToInvoice"/>.
    /// </summary>
    public Money RefundToCustomer(Money amount, DateTime occurredOnUtc)
    {
        if (amount.IsNegative || amount.IsZero)
            throw new DomainException("A credit balance movement requires a positive amount.");

        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("An app credit balance timestamp must be UTC.");

        if (amount > Balance)
            throw new DomainException("The app credit balance is lower than the requested amount.");

        Balance = Balance.Subtract(amount);
        UpdatedOnUtc = occurredOnUtc;
        return Balance;
    }
}
