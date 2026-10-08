using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverPayoutTests
{
    [Fact]
    public void Record_ShouldSnapshotAmountsWithNetEqualToGross()
    {
        CaregiverPayout payout = CaregiverPayout.Record(
            new BookingId(Guid.CreateVersion7()),
            new CaregiverId(Guid.CreateVersion7()),
            150m,
            22.50m,
            "EGP",
            2,
            1,
            "nbe",
            "****0002",
            "BANK-2026-000123",
            "Transfer advice 000123",
            "October weekly payout",
            new UserId(Guid.CreateVersion7()),
            Utc(10));

        Assert.Equal(150m, payout.GrossAmount);
        Assert.Equal(22.50m, payout.PlatformFeeAmount);
        Assert.Equal(payout.GrossAmount, payout.NetAmount);
        Assert.Equal("EGP", payout.Currency);
        Assert.Equal(2, payout.PolicyVersion);
        Assert.Equal(1, payout.ChargeRuleVersion);
        Assert.Equal("NBE", payout.BankCode);
        Assert.Equal("****0002", payout.MaskedIban);
        Assert.Equal(PayoutStatus.Paid, payout.Status);
        Assert.Null(payout.FailureReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Record_ShouldRejectNonPositiveGross(decimal grossAmount)
    {
        Assert.Throws<DomainException>(() => RecordWith(grossAmount, 22.50m));
    }

    [Fact]
    public void Record_ShouldRejectNegativePlatformFee()
    {
        Assert.Throws<DomainException>(() => RecordWith(150m, -1m));
    }

    [Fact]
    public void MarkFailed_ShouldMovePaidPayoutToFailed()
    {
        CaregiverPayout payout = RecordWith(150m, 22.50m);
        var actor = new UserId(Guid.CreateVersion7());

        payout.MarkFailed("Bank rejected the transfer", actor, Utc(11));

        Assert.Equal(PayoutStatus.Failed, payout.Status);
        Assert.Equal("Bank rejected the transfer", payout.FailureReason);
        Assert.Equal(actor, payout.FailedBy);
        Assert.Equal(Utc(11), payout.FailedOnUtc);
    }

    [Fact]
    public void MarkFailed_ShouldRejectSecondTransition()
    {
        CaregiverPayout payout = RecordWith(150m, 22.50m);
        var actor = new UserId(Guid.CreateVersion7());
        payout.MarkFailed("Bank rejected the transfer", actor, Utc(11));

        Assert.Throws<DomainException>(
            () => payout.MarkFailed("Again", actor, Utc(12)));
    }

    private static CaregiverPayout RecordWith(decimal grossAmount, decimal platformFeeAmount) =>
        CaregiverPayout.Record(
            new BookingId(Guid.CreateVersion7()),
            new CaregiverId(Guid.CreateVersion7()),
            grossAmount,
            platformFeeAmount,
            "EGP",
            2,
            1,
            "NBE",
            "****0002",
            "BANK-2026-000123",
            "Transfer advice 000123",
            "October weekly payout",
            new UserId(Guid.CreateVersion7()),
            Utc(10));

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);
}
