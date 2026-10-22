namespace Sanad.Modules.CareHomes.Domain.Bookings;

/// <summary>
/// The refund-policy facts a booking snapshots at creation — the revision it
/// was sold under and, when the facility had published structured cancellation
/// terms on its approved profile, the exact terms displayed at that moment
/// together with their provenance.
/// <para>
/// Created by the application layer (which can see the facility's approved
/// profile revision) and held immutably by the booking. A null snapshot means
/// "legacy": the booking decides under its pre-policy behavior. A snapshot
/// with <see cref="CancellationTermsJson"/> present must be a revision-2
/// snapshot; a revision-2 snapshot without terms dispatches to the clause 2
/// default schedule.
/// </para>
/// </summary>
public sealed record CareHomePolicySnapshot(
    int PolicyVersion,
    DateOnly DocumentEffectiveOn,
    string? CancellationTermsJson,
    Guid? ProfileRevisionId)
{
    /// <summary>
    /// Builds a validated snapshot. Throws <see cref="Sanad.BuildingBlocks.Domain.Exceptions.DomainException"/>
    /// when the version/document pair is not a known policy revision or a
    /// version-2 snapshot carries unreadable terms — a booking may never be
    /// persisted with a policy snapshot its own policy cannot re-read.
    /// </summary>
    public static CareHomePolicySnapshot From(
        CareHomeRefundPolicyVersion version,
        Guid? profileRevisionId,
        string? cancellationTermsJson)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.IsScheduledStart && cancellationTermsJson is not null)
        {
            // Parse validates the JSON shape; the values are not re-serialized so
            // the snapshot stays byte-identical to what the profile stored.
            CareHomeCancellationTerms.Parse(cancellationTermsJson);
        }

        if (version.IsLegacy && cancellationTermsJson is not null)
        {
            throw new Sanad.BuildingBlocks.Domain.Exceptions.DomainException(
                "Legacy care-home bookings do not carry structured cancellation-term snapshots.");
        }

        return new CareHomePolicySnapshot(version.Version, version.DocumentEffectiveOnUtc, cancellationTermsJson, profileRevisionId);
    }
}
