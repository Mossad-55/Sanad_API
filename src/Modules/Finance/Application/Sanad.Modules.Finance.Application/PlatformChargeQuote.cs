using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Application;

public sealed record PlatformChargeQuote(decimal BaseAmount, decimal PlatformFeeRatePercentage, decimal PlatformFeeAmount, decimal TaxRatePercentage, decimal TaxAmount, decimal TotalAmount, string Currency);

public interface IPlatformChargeRuleReader
{
    Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken);
}

public sealed record PlatformChargeRuleRates(decimal PlatformFeeRatePercentage, decimal TaxRatePercentage, int Version);
public sealed record PlatformChargeRuleHistoryItem(Guid Id, decimal PlatformFeeRatePercentage, decimal TaxRatePercentage, int Version, DateTime EffectiveOnUtc, DateTime CreatedOnUtc, bool IsActive);

public static class PlatformChargeCalculator
{
    public static PlatformChargeQuote Calculate(decimal baseAmount, decimal feeRate, decimal taxRate, string currency = "EGP")
    {
        if (baseAmount <= 0) throw new DomainException("Base amount must be greater than zero.");
        if (feeRate is < 0 or > 100 || taxRate is < 0 or > 100) throw new DomainException("Platform charge rates must be between 0 and 100 percent.");
        var basis = Money(baseAmount);
        var fee = Money(basis * feeRate / 100m);
        var tax = Money(basis * taxRate / 100m);
        return new(basis, Money(feeRate), fee, Money(taxRate), tax, Money(basis + fee + tax), currency);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}
