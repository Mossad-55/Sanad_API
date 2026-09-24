namespace Sanad.Modules.Families.Domain.Subscriptions;

public enum SubscriptionInvoiceKind { InitialPurchase = 1, Renewal = 2 }

public sealed class SubscriptionInvoice
{
    private SubscriptionInvoice() { }
    private SubscriptionInvoice(Guid id, string number, Guid familyId, Guid? attemptId, Guid subscriptionId, SubscriptionInvoiceKind kind, string planKey, int planVersion, decimal basePrice, decimal discountAmount, decimal taxAmount, decimal total, string currency, DateTime periodStart, DateTime periodEnd, DateTime issued, string pdfKey, string? providerEventId)
    {
        Id = id; InvoiceNumber = number; OwnerFamilyId = familyId; PaymentAttemptId = attemptId; SubscriptionId = subscriptionId; Kind = kind; PlanKey = planKey; PlanVersion = planVersion; BasePrice = basePrice; DiscountAmount = discountAmount; TaxAmount = taxAmount; TotalPayable = total; Currency = currency; PeriodStartsOnUtc = periodStart; PeriodEndsOnUtc = periodEnd; IssuedOnUtc = issued; PdfStorageKey = pdfKey; ProviderEventId = providerEventId;
    }
    public Guid Id { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid OwnerFamilyId { get; private set; }
    public Guid? PaymentAttemptId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public SubscriptionInvoiceKind Kind { get; private set; }
    public string PlanKey { get; private set; } = string.Empty;
    public int PlanVersion { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalPayable { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTime PeriodStartsOnUtc { get; private set; }
    public DateTime PeriodEndsOnUtc { get; private set; }
    public DateTime IssuedOnUtc { get; private set; }
    public string PdfStorageKey { get; private set; } = string.Empty;
    public string? ProviderEventId { get; private set; }

    public static SubscriptionInvoice Create(string number, Guid familyId, Guid? attemptId, Guid subscriptionId, SubscriptionInvoiceKind kind, string planKey, int planVersion, decimal basePrice, decimal discountAmount, decimal taxAmount, decimal total, string currency, DateTime periodStart, DateTime periodEnd, DateTime issued, string pdfKey, string? providerEventId)
    {
        if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(planKey) || string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(pdfKey)) throw new ArgumentException("Invoice identity, plan, currency, and PDF are required.");
        if (issued.Kind != DateTimeKind.Utc || periodStart.Kind != DateTimeKind.Utc || periodEnd.Kind != DateTimeKind.Utc) throw new ArgumentException("Invoice timestamps must be UTC.");
        return new SubscriptionInvoice(Guid.CreateVersion7(), number.Trim(), familyId, attemptId, subscriptionId, kind, planKey.Trim(), planVersion, basePrice, discountAmount, taxAmount, total, currency.Trim(), periodStart, periodEnd, issued, pdfKey.Trim(), providerEventId?.Trim());
    }
}
