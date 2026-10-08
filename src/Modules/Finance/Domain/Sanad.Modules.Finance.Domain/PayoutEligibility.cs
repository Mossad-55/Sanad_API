namespace Sanad.Modules.Finance.Domain;

public static class PayoutEligibility
{
    public sealed record Result(bool IsEligible, DateTime? EligibleAfterUtc);

    public static Result Evaluate(
        DateTime? completedOnUtc,
        DateTime? paidOnUtc,
        int payoutDelayHours,
        DateTime utcNow)
    {
        if (completedOnUtc is null || paidOnUtc is null)
            return new Result(false, null);

        if (completedOnUtc.Value.Kind != DateTimeKind.Utc ||
            paidOnUtc.Value.Kind != DateTimeKind.Utc ||
            utcNow.Kind != DateTimeKind.Utc ||
            payoutDelayHours < 0)
            return new Result(false, null);

        DateTime eligibleAfterUtc =
            (completedOnUtc.Value > paidOnUtc.Value ? completedOnUtc.Value : paidOnUtc.Value)
                .AddHours(payoutDelayHours);

        return new Result(utcNow >= eligibleAfterUtc, eligibleAfterUtc);
    }
}
