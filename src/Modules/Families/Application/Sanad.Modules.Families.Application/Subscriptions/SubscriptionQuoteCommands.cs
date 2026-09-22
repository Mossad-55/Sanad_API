using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record CreateSubscriptionQuoteCommand(
    UserId UserId,
    Guid PlanVersionId,
    string? CouponCode,
    DateTime UtcNow) : IQuery<SubscriptionQuoteResponse>;

public sealed record SubscriptionQuoteResponse(
    Guid PlanVersionId,
    string PlanKey,
    int PlanVersion,
    SubscriptionCycle Cycle,
    string Currency,
    decimal BasePrice,
    string? CouponCode,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal TaxableAmount,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    decimal TotalPayable,
    bool RecurringRenewalSupported);

public sealed class CreateSubscriptionQuoteQueryHandler
    : IQueryHandler<CreateSubscriptionQuoteCommand, SubscriptionQuoteResponse>
{
    private static readonly Error NotOwner = new(
        "Subscriptions.NotOwner", "Only the family owner can request a subscription quote.");
    private static readonly Error PlanNotFound = new(
        "Subscriptions.Quote.PlanNotFound", "The requested subscription plan is not available for purchase.");
    private static readonly Error CouponInvalid = new(
        "Subscriptions.Quote.CouponInvalid", "The coupon is invalid for the requested subscription plan.");
    private static readonly Error TaxNotConfigured = new(
        "Subscriptions.Quote.TaxNotConfigured", "Subscription tax configuration is not available.");

    private readonly IFamiliesDbContext _db;

    public CreateSubscriptionQuoteQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<SubscriptionQuoteResponse>> Handle(
        CreateSubscriptionQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId))
            return Result<SubscriptionQuoteResponse>.Failure(NotOwner);

        var plan = await _db.SubscriptionPlanVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.PlanVersionId
                    && item.IsPublished
                    && item.IsAvailableForNewSales,
                cancellationToken);

        if (plan is null)
            return Result<SubscriptionQuoteResponse>.Failure(PlanNotFound);

        decimal discountPercentage = 0m;
        string? couponCode = null;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            couponCode = request.CouponCode.Trim().ToUpperInvariant();
            var coupon = await _db.SubscriptionCoupons
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Code == couponCode, cancellationToken);

            if (coupon is null
                || coupon.PlanVersionId != plan.Id
                || request.UtcNow >= coupon.ExpiresOnUtc)
                return Result<SubscriptionQuoteResponse>.Failure(CouponInvalid);

            discountPercentage = coupon.DiscountPercentage;
        }

        var taxRule = await _db.SubscriptionTaxRules
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IsActive && item.EffectiveOnUtc <= request.UtcNow,
                cancellationToken);

        if (taxRule is null)
            return Result<SubscriptionQuoteResponse>.Failure(TaxNotConfigured);

        decimal basePrice = Money(plan.Price);
        decimal discountAmount = Money(basePrice * discountPercentage / 100m);
        decimal taxableAmount = Money(basePrice - discountAmount);
        decimal taxAmount = Money(taxableAmount * taxRule.RatePercentage / 100m);
        decimal totalPayable = Money(taxableAmount + taxAmount);

        return new SubscriptionQuoteResponse(
            plan.Id,
            plan.Key,
            plan.Version,
            plan.Cycle,
            plan.Currency,
            basePrice,
            couponCode,
            discountPercentage,
            discountAmount,
            taxableAmount,
            Money(taxRule.RatePercentage),
            taxAmount,
            totalPayable,
            RecurringRenewalSupported: false);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.ToEven);
}
