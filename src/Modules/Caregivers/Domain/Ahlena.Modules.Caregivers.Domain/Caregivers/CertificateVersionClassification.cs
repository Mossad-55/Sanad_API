namespace Ahlena.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// A derived (never stored) grouping of a certificate version for the owner /
/// admin read surfaces. The client can filter on this axis instead of
/// re-deriving it from the raw flags:
/// <list type="bullet">
/// <item><see cref="Current"/> — the approved, effective version backing the
/// certificate's columns (even when a later Cairo date has made it
/// expired).</item>
/// <item><see cref="Pending"/> — the single upload awaiting a review decision
/// (an initial pending version or a pending replacement).</item>
/// <item><see cref="History"/> — a superseded or rejected version retained for
/// audit.</item>
/// </list>
/// A soft-removed version is never classified here: it is hidden from the read
/// surfaces entirely.
/// </summary>
public enum CertificateVersionClassification
{
    Current = 1,
    Pending = 2,
    History = 3
}
