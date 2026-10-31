namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// Why a subscription money movement is being recorded. Clause 9 keeps three
/// economically different outcomes apart, and the contract requires
/// "subscription cancellation, duplicate charge and shutdown must have distinct
/// reasons and outcomes": an erroneous or duplicate charge is refunded
/// <em>in full</em>, an Ahlena service shutdown refunds the <em>unused
/// portion</em> of the current paid period, and an ordinary cancel-renewal
/// refunds <em>nothing</em> for the current period.
/// </summary>
public enum SubscriptionRefundReason
{
    /// <summary>
    /// Ordinary policy cancel-renewal. Access continues to the end of the paid
    /// period and no current-period or partial-period refund is produced; the
    /// movement is recorded as a zero-piastre award so the outcome is still
    /// attributable and distinct from the other two reasons.
    /// </summary>
    CancelRenewal = 1,

    /// <summary>Clause 9 / decision #6: an erroneous or duplicate charge is refunded in full, never prorated.</summary>
    DuplicateCharge = 2,

    /// <summary>Clause 9 / decision #6: Ahlena service shutdown refunds the unused duration of the current paid period.</summary>
    ServiceShutdown = 3
}
