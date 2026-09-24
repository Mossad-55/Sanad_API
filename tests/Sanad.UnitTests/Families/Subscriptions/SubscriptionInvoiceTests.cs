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
        byte[] pdf = SubscriptionInvoicePdfRenderer.Render("INV-2026-09-000001", SubscriptionInvoiceKind.InitialPurchase, "Premium", 1, 300m, 0m, 45m, 345m, "EGP", start, end, start);
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
        Assert.Throws<ArgumentException>(() => SubscriptionInvoice.Create("INV-2026-09-000001", Guid.NewGuid(), null, Guid.NewGuid(), SubscriptionInvoiceKind.Renewal, "Premium", 1, 300m, 0m, 45m, 345m, "EGP", utc, utc, utc, string.Empty, "evt-1"));
        Assert.Throws<ArgumentException>(() => SubscriptionInvoice.Create("INV-2026-09-000001", Guid.NewGuid(), null, Guid.NewGuid(), SubscriptionInvoiceKind.Renewal, "Premium", 1, 300m, 0m, 45m, 345m, "EGP", utc.ToLocalTime(), utc, utc, "private/invoices/a.pdf", "evt-1"));
    }
}
