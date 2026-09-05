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

    public decimal BaseCaregiverFee { get; private set; }
    public decimal PlatformFeePercentage { get; private set; }
    public decimal PlatformFeeAmount { get; private set; }
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

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BaseCaregiverFee;
        yield return PlatformFeePercentage;
        yield return PlatformFeeAmount;
        yield return TotalPayableAmount;
        yield return Currency;
    }
}