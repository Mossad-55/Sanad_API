namespace Sanad.Modules.CareHomes.Domain.Facilities;

public readonly record struct CareHomeId(Guid Value) : IComparable<CareHomeId>, IComparable
{
    public static CareHomeId New() => new(Guid.CreateVersion7());
    public static CareHomeId Empty => new(Guid.Empty);
    public int CompareTo(CareHomeId other) => Value.CompareTo(other.Value);
    int IComparable.CompareTo(object? obj) => obj is CareHomeId other
        ? CompareTo(other)
        : throw new ArgumentException($"Object must be of type {nameof(CareHomeId)}.", nameof(obj));
    public override string ToString() => Value.ToString();
}

public enum CareHomeStatus
{
    Draft = 1,
    PendingReview = 2,
    NeedsCorrection = 3,
    Rejected = 4,
    Approved = 5,
    Suspended = 6
}

public enum CareHomeDocumentType
{
    OperatingLicense = 1,
    Registration = 2,
    HealthCertificate = 3,
    CivilDefenseCertificate = 4
}

public enum CareHomeDocumentStatus
{
    PendingReview = 1,
    Verified = 2,
    Rejected = 3,
    Revoked = 4
}

public enum CareHomeReviewAction
{
    Approved = 1,
    Rejected = 2,
    CorrectionsRequested = 3,
    Suspended = 4,
    Reactivated = 5
}
