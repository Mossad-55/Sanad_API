using System.Text;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Application.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionInvoiceTests
{
    [Fact]
    public void Pdf_renderer_returns_a_branded_pdf_with_invoice_contract_fields()
    {
        DateTime start = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        byte[] pdf = SubscriptionInvoicePdfRenderer.Render("INV-2026-09-000001", SubscriptionInvoiceKind.InitialPurchase, "Premium", 1, 300m, 0m, 45m, 0m, 345m, "EGP", start, end, start);
        string content = Encoding.ASCII.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", content);
        Assert.Contains("SANAD CARE", content);
        Assert.Contains("INV-2026-09-000001", content);
        Assert.Contains("TOTAL: 345.00 EGP", content);
        Assert.EndsWith("%%EOF\n", content);
    }

    [Fact]
    public void Invoice_requires_utc_timestamps_and_pdf_storage_key()
    {
        DateTime utc = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => SubscriptionInvoice.Create("INV-2026-09-000001", Guid.NewGuid(), null, Guid.NewGuid(), SubscriptionInvoiceKind.Renewal, "Premium", 1, 300m, 0m, 15m, 45m, 30m, 2, 375m, "EGP", utc, utc, utc, string.Empty, "evt-1"));
        Assert.Throws<ArgumentException>(() => SubscriptionInvoice.Create("INV-2026-09-000001", Guid.NewGuid(), null, Guid.NewGuid(), SubscriptionInvoiceKind.Renewal, "Premium", 1, 300m, 0m, 15m, 45m, 30m, 2, 375m, "EGP", utc.ToLocalTime(), utc, utc, "private/invoices/a.pdf", "evt-1"));
    }

    [Fact]
    public void Invoice_preserves_platform_charge_snapshot_and_provider_event_link()
    {
        DateTime utc = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        var invoice = SubscriptionInvoice.Create("INV-2026-09-000002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SubscriptionInvoiceKind.Renewal, "Premium", 2, 300m, 10m, 12.5m, 36.25m, 22.58m, 9, 369m, "EGP", utc, utc.AddMonths(1), utc, "private/invoices/b.pdf", "provider-event-9");

        Assert.Equal(12.5m, invoice.PlatformFeeRatePercentage);
        Assert.Equal(36.25m, invoice.PlatformFeeAmount);
        Assert.Equal(22.58m, invoice.TaxAmount);
        Assert.Equal(9, invoice.PlatformChargeRuleVersion);
        Assert.Equal(369m, invoice.TotalPayable);
        Assert.Equal("provider-event-9", invoice.ProviderEventId);
        Assert.NotNull(invoice.PaymentAttemptId);
    }
}
