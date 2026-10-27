namespace Sanad.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// A derived (never stored) view of a certificate version's expiry. It is
/// computed against the current Cairo calendar date on every read.
/// </summary>
/// <remarks>
/// <see cref="None"/> is the honest value for a version with no recorded
/// expiry date: the legacy model accepted an absent date as "no expiry
/// recorded", so it is labelled as such rather than presented as a valid or an
/// admin-confirmed non-expiring approval. No date is ever invented.
/// </remarks>
public enum CertificateExpiryStatus
{
    None = 0,
    Valid = 1,
    Expired = 2
}
