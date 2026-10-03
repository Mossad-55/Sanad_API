using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.BuildingBlocks.Domain.ValueObjects;

public sealed class BookingPriceSnapshot : ValueObject
{
    public const string DefaultCurrency = "EGP";

    private BookingPriceSnapshot()
    {
    }

    private BookingPriceSnapshot(
        decimal baseCaregiverFee,
        decimal platformFeePercentage,
        decimal platformFeeAmount,
        decimal totalPayableAmount,
        string currency)
    {
        BaseCaregiverFee = baseCaregiverFee;
        PlatformFeePercentage = platformFeePercentage;
        PlatformFeeAmount = platformFeeAmount;
        TotalPayableAmount = totalPayableAmount;
        Currency = currency;
    }

    private BookingPriceSnapshot(decimal baseFee, decimal feeRate, decimal feeAmount, decimal taxRate, decimal taxAmount, int ruleVersion, decimal total, string currency)
    {
        BaseCaregiverFee = baseFee; PlatformFeePercentage = feeRate; PlatformFeeAmount = feeAmount;
        TaxRatePercentage = taxRate; TaxAmount = taxAmount; PlatformChargeRuleVersion = ruleVersion;
        TotalPayableAmount = total; Currency = currency;
    }

    public decimal BaseCaregiverFee { get; private set; }
    public decimal PlatformFeePercentage { get; private set; }
    public decimal PlatformFeeAmount { get; private set; }
    public decimal TaxRatePercentage { get; private set; }
    public decimal TaxAmount { get; private set; }
    public int? PlatformChargeRuleVersion { get; private set; }
    public decimal TotalPayableAmount { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;

    public static BookingPriceSnapshot Calculate(
        decimal baseCaregiverFee,
        decimal platformFeePercentage,
        string currency = DefaultCurrency)
    {
        if (baseCaregiverFee <= 0)
        {
            throw new DomainException("Base caregiver fee must be greater than zero.");
        }

        if (platformFeePercentage < 0 || platformFeePercentage > 100)
        {
            throw new DomainException("Platform fee percentage must be between 0 and 100.");
        }

        decimal roundedBaseFee = decimal.Round(baseCaregiverFee, 2, MidpointRounding.ToEven);
        decimal roundedPercentage = decimal.Round(platformFeePercentage, 2, MidpointRounding.ToEven);

        decimal feeAmount = decimal.Round(roundedBaseFee * (roundedPercentage / 100m), 2, MidpointRounding.ToEven);
        decimal totalAmount = roundedBaseFee + feeAmount;

        return new BookingPriceSnapshot(
            roundedBaseFee,
            roundedPercentage,
            feeAmount,
            totalAmount,
            currency);
    }

    public static BookingPriceSnapshot Calculate(decimal baseFee, decimal feeRate, decimal taxRate, int ruleVersion, string currency = DefaultCurrency)
    {
        if (baseFee <= 0 || feeRate is < 0 or > 100 || taxRate is < 0 or > 100 || ruleVersion <= 0)
            throw new DomainException("Booking charge configuration is invalid.");
        decimal basis = decimal.Round(baseFee, 2, MidpointRounding.ToEven);
        decimal fee = decimal.Round(basis * feeRate / 100m, 2, MidpointRounding.ToEven);
        decimal tax = decimal.Round(basis * taxRate / 100m, 2, MidpointRounding.ToEven);
        return new(basis, decimal.Round(feeRate, 2), fee, decimal.Round(taxRate, 2), tax, ruleVersion, decimal.Round(basis + fee + tax, 2, MidpointRounding.ToEven), currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BaseCaregiverFee;
        yield return PlatformFeePercentage;
        yield return PlatformFeeAmount;
        yield return TaxRatePercentage;
        yield return TaxAmount;
        yield return PlatformChargeRuleVersion;
        yield return TotalPayableAmount;
        yield return Currency;
    }
}
