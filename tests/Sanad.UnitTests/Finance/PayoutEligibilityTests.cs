using Sanad.Modules.Finance.Domain;

namespace Sanad.UnitTests.Finance;

public sealed class PayoutEligibilityTests
{
    [Fact]
    public void Evaluate_ShouldRequireBothCompletedAndPaid()
    {
        var now = Utc(10);

        Assert.False(PayoutEligibility.Evaluate(null, Utc(1), 72, now).IsEligible);
        Assert.False(PayoutEligibility.Evaluate(Utc(1), null, 72, now).IsEligible);
        Assert.False(PayoutEligibility.Evaluate(null, null, 72, now).IsEligible);
    }

    [Fact]
    public void Evaluate_ShouldFailClosed_WhenTimestampsAreMissing()
    {
        var result = PayoutEligibility.Evaluate(Utc(1), null, 72, Utc(10));

        Assert.False(result.IsEligible);
        Assert.Null(result.EligibleAfterUtc);
    }

    [Fact]
    public void Evaluate_ShouldUseLaterTimestampPlusDelay()
    {
        // Paid after completion: eligibility anchors on the payment.
        var result = PayoutEligibility.Evaluate(Utc(1), Utc(2), 72, Utc(10));

        Assert.Equal(Utc(2).AddHours(72), result.EligibleAfterUtc);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_ShouldUseCompletion_WhenItIsLater()
    {
        var result = PayoutEligibility.Evaluate(Utc(5), Utc(2), 72, Utc(10));

        Assert.Equal(Utc(5).AddHours(72), result.EligibleAfterUtc);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_ShouldBeIneligible_BeforeDelayPasses()
    {
        var result = PayoutEligibility.Evaluate(Utc(5), Utc(2), 72, Utc(7));

        Assert.Equal(Utc(5).AddHours(72), result.EligibleAfterUtc);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_ShouldBeEligibleExactlyAtDelayBoundary()
    {
        DateTime eligibleAfter = Utc(2).AddHours(72);

        var result = PayoutEligibility.Evaluate(Utc(1), Utc(2), 72, eligibleAfter);

        Assert.True(result.IsEligible);
        Assert.Equal(eligibleAfter, result.EligibleAfterUtc);
    }

    [Fact]
    public void Evaluate_ShouldAllowZeroDelay()
    {
        var result = PayoutEligibility.Evaluate(Utc(5), Utc(5), 0, Utc(5));

        Assert.True(result.IsEligible);
        Assert.Equal(Utc(5), result.EligibleAfterUtc);
    }

    [Fact]
    public void Evaluate_ShouldFailClosed_WhenDelayIsNegative()
    {
        var result = PayoutEligibility.Evaluate(Utc(1), Utc(1), -1, Utc(10));

        Assert.False(result.IsEligible);
        Assert.Null(result.EligibleAfterUtc);
    }

    [Fact]
    public void Evaluate_ShouldFailClosed_WhenDelayOverflows()
    {
        var result = PayoutEligibility.Evaluate(Utc(1), Utc(1), int.MaxValue, Utc(10));

        Assert.False(result.IsEligible);
        Assert.Null(result.EligibleAfterUtc);
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);
}
