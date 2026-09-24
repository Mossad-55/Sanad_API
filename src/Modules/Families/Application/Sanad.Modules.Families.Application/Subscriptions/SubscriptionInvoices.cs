using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record SubscriptionInvoiceListItemResponse(Guid Id, string InvoiceNumber, SubscriptionInvoiceKind Kind, decimal Amount, string Currency, DateTime IssuedOnUtc, DateTime PeriodStartsOnUtc, DateTime PeriodEndsOnUtc);
public sealed record SubscriptionInvoiceResponse(Guid Id, string InvoiceNumber, SubscriptionInvoiceKind Kind, decimal BasePrice, decimal DiscountAmount, decimal TaxAmount, decimal Amount, string Currency, DateTime IssuedOnUtc, DateTime PeriodStartsOnUtc, DateTime PeriodEndsOnUtc);
public sealed record SubscriptionInvoiceFile(string Key, string ContentType, Stream Content);

public interface ISubscriptionInvoiceService
{
    Task<Result> CreateInitialAsync(SubscriptionPaymentAttempt attempt, FamilySubscription subscription, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken);
    Task<Result> CreateRenewalAsync(FamilySubscription subscription, decimal total, decimal taxRate, DateTime periodStart, DateTime periodEnd, string providerEventId, CancellationToken cancellationToken);
}

public sealed class SubscriptionInvoiceService : ISubscriptionInvoiceService
{
    private readonly IFamiliesDbContext _db;
    private readonly IFileStorage _storage;
    public SubscriptionInvoiceService(IFamiliesDbContext db, IFileStorage storage) { _db = db; _storage = storage; }

    public Task<Result> CreateInitialAsync(SubscriptionPaymentAttempt attempt, FamilySubscription subscription, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken) =>
        CreateAsync(attempt.FamilyId.Value, attempt.Id, subscription, SubscriptionInvoiceKind.InitialPurchase, attempt.BasePrice, attempt.DiscountAmount, attempt.TaxAmount, attempt.TotalPayable, attempt.Currency, periodStart, periodEnd, null, cancellationToken);

    public Task<Result> CreateRenewalAsync(FamilySubscription subscription, decimal total, decimal taxRate, DateTime periodStart, DateTime periodEnd, string providerEventId, CancellationToken cancellationToken)
    {
        decimal taxAmount = decimal.Round(total - total / (1m + taxRate / 100m), 2, MidpointRounding.ToEven);
        decimal basePrice = decimal.Round(total - taxAmount, 2, MidpointRounding.ToEven);
        return CreateAsync(subscription.FamilyId.Value, null, subscription, SubscriptionInvoiceKind.Renewal, basePrice, 0m, taxAmount, total, subscription.Currency, periodStart, periodEnd, providerEventId, cancellationToken);
    }

    private async Task<Result> CreateAsync(Guid familyId, Guid? attemptId, FamilySubscription subscription, SubscriptionInvoiceKind kind, decimal basePrice, decimal discountAmount, decimal taxAmount, decimal total, string currency, DateTime periodStart, DateTime periodEnd, string? providerEventId, CancellationToken cancellationToken)
    {
        if (attemptId is Guid paymentAttemptId && await _db.SubscriptionInvoices.AnyAsync(x => x.PaymentAttemptId == paymentAttemptId, cancellationToken)) return Result.Success();
        if (providerEventId is not null && await _db.SubscriptionInvoices.AnyAsync(x => x.ProviderEventId == providerEventId, cancellationToken)) return Result.Success();

        DateTime issued = DateTime.UtcNow;
        string prefix = $"INV-{issued:yyyy-MM}-";
        int next = await _db.SubscriptionInvoices.AsNoTracking().Where(x => x.InvoiceNumber.StartsWith(prefix)).Select(x => x.InvoiceNumber).ToListAsync(cancellationToken) is { } numbers
            ? numbers.Select(x => int.TryParse(x[prefix.Length..], out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1
            : 1;
        string number = prefix + next.ToString("D6", CultureInfo.InvariantCulture);
        byte[] pdf = SubscriptionInvoicePdfRenderer.Render(number, kind, subscription.PlanKey, subscription.PlanVersion, basePrice, discountAmount, taxAmount, total, currency, periodStart, periodEnd, issued);
        await using var stream = new MemoryStream(pdf, writable: false);
        var stored = await _storage.SavePrivateAsync(stream, "application/pdf", pdf.Length, "subscription-invoices", cancellationToken);
        if (stored.IsFailure) return Result.Failure(stored.Error);
        _db.SubscriptionInvoices.Add(SubscriptionInvoice.Create(number, familyId, attemptId, subscription.Id, kind, subscription.PlanKey, subscription.PlanVersion, basePrice, discountAmount, taxAmount, total, currency, periodStart, periodEnd, issued, stored.Value.Key, providerEventId));
        return Result.Success();
    }
}

public sealed record GetFamilySubscriptionInvoicesQuery(UserId UserId) : BuildingBlocks.Application.CQRS.IQuery<IReadOnlyList<SubscriptionInvoiceListItemResponse>>;
public sealed class GetFamilySubscriptionInvoicesQueryHandler : BuildingBlocks.Application.CQRS.IQueryHandler<GetFamilySubscriptionInvoicesQuery, IReadOnlyList<SubscriptionInvoiceListItemResponse>>
{
    private readonly IFamiliesDbContext _db;
    public GetFamilySubscriptionInvoicesQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<IReadOnlyList<SubscriptionInvoiceListItemResponse>>> Handle(GetFamilySubscriptionInvoicesQuery request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result<IReadOnlyList<SubscriptionInvoiceListItemResponse>>.Failure(new Error("Subscriptions.Invoice.NotOwner", "Only the family owner can view subscription invoices."));
        var rows = await _db.SubscriptionInvoices.AsNoTracking().Where(x => x.OwnerFamilyId == family.Id.Value).OrderByDescending(x => x.IssuedOnUtc).Select(x => new SubscriptionInvoiceListItemResponse(x.Id, x.InvoiceNumber, x.Kind, x.TotalPayable, x.Currency, x.IssuedOnUtc, x.PeriodStartsOnUtc, x.PeriodEndsOnUtc)).ToListAsync(cancellationToken);
        return rows;
    }
}

public sealed record GetFamilySubscriptionInvoiceQuery(UserId UserId, Guid InvoiceId) : BuildingBlocks.Application.CQRS.IQuery<SubscriptionInvoiceResponse>;
public sealed record GetInvoiceStorageKeyQuery(UserId UserId, Guid InvoiceId) : BuildingBlocks.Application.CQRS.IQuery<string>;
public sealed class GetInvoiceStorageKeyQueryHandler : BuildingBlocks.Application.CQRS.IQueryHandler<GetInvoiceStorageKeyQuery, string>
{
    private readonly IFamiliesDbContext _db;
    public GetInvoiceStorageKeyQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<string>> Handle(GetInvoiceStorageKeyQuery request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result<string>.Failure(new Error("Subscriptions.Invoice.NotOwner", "Only the family owner can download subscription invoices."));
        var key = await _db.SubscriptionInvoices.AsNoTracking().Where(x => x.Id == request.InvoiceId && x.OwnerFamilyId == family.Id.Value).Select(x => x.PdfStorageKey).SingleOrDefaultAsync(cancellationToken);
        return key is null ? Result<string>.Failure(new Error("Subscriptions.Invoice.NotFound", "The subscription invoice was not found.")) : key;
    }
}

public sealed class GetFamilySubscriptionInvoiceQueryHandler : BuildingBlocks.Application.CQRS.IQueryHandler<GetFamilySubscriptionInvoiceQuery, SubscriptionInvoiceResponse>
{
    private readonly IFamiliesDbContext _db;
    public GetFamilySubscriptionInvoiceQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<SubscriptionInvoiceResponse>> Handle(GetFamilySubscriptionInvoiceQuery request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result<SubscriptionInvoiceResponse>.Failure(new Error("Subscriptions.Invoice.NotOwner", "Only the family owner can view subscription invoices."));
        var x = await _db.SubscriptionInvoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == request.InvoiceId && i.OwnerFamilyId == family.Id.Value, cancellationToken);
        return x is null ? Result<SubscriptionInvoiceResponse>.Failure(new Error("Subscriptions.Invoice.NotFound", "The subscription invoice was not found.")) : new SubscriptionInvoiceResponse(x.Id, x.InvoiceNumber, x.Kind, x.BasePrice, x.DiscountAmount, x.TaxAmount, x.TotalPayable, x.Currency, x.IssuedOnUtc, x.PeriodStartsOnUtc, x.PeriodEndsOnUtc);
    }
}

public static class SubscriptionInvoicePdfRenderer
{
    public static byte[] Render(string number, SubscriptionInvoiceKind kind, string planKey, int planVersion, decimal basePrice, decimal discount, decimal tax, decimal total, string currency, DateTime periodStart, DateTime periodEnd, DateTime issued)
    {
        string[] lines = { "SANAD CARE", "SUBSCRIPTION INVOICE", number, kind == SubscriptionInvoiceKind.InitialPurchase ? "Initial purchase" : "Successful renewal", $"Plan: {planKey} v{planVersion}", $"Service period: {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}", $"Issued: {issued:yyyy-MM-dd HH:mm} UTC", $"Subtotal: {basePrice:0.00} {currency}", $"Discount: {discount:0.00} {currency}", $"Tax: {tax:0.00} {currency}", $"TOTAL: {total:0.00} {currency}", "Thank you for choosing Sanad Care." };
        var content = new StringBuilder("q\n0.06 0.33 0.39 rg\n40 720 515 60 re f\nQ\nBT\n/F1 22 Tf\n1 1 1 rg\n55 752 Td\n(SANAD CARE) Tj\n/F1 12 Tf\n0 0 0 rg\n55 695 Td\n");
        for (int i = 1; i < lines.Length; i++) { content.Append(i == lines.Length - 2 ? "/F1 16 Tf\n" : "/F1 11 Tf\n"); content.Append('(').Append(Escape(lines[i])).Append(") Tj\n0 -32 Td\n"); }
        content.Append("ET\n");
        byte[] body = Encoding.ASCII.GetBytes(content.ToString());
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        void Obj(int n, string value) { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append(n).Append(" 0 obj\n").Append(value).Append("\nendobj\n"); }
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>"); Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"); Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"); Obj(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"); Obj(5, $"<< /Length {body.Length} >>\nstream\n{Encoding.ASCII.GetString(body)}endstream"); int xref = Encoding.ASCII.GetByteCount(pdf.ToString()); pdf.Append("xref\n0 6\n0000000000 65535 f \n"); for (int i = 1; i <= 5; i++) pdf.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n"); pdf.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n"); return Encoding.ASCII.GetBytes(pdf.ToString());
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
