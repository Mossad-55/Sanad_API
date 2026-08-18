using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Events;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class Caregiver : AggregateRoot<CaregiverId>
{
    private Caregiver()
    {
    }

    private Caregiver(
        CaregiverId id,
        UserId userId,
        CaregiverType type)
        : base(id)
    {
        UserId = userId;
        Type = type;
        Status = CaregiverStatus.PendingVerification;
        Availability = CaregiverAvailability.Unavailable;
        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(
            new CaregiverCreatedDomainEvent(
                Id,
                UserId));
    }

    public UserId UserId { get; private set; }
    public CaregiverProfile? Profile { get; private set; }
    public CaregiverType Type { get; private set; }
    public CaregiverStatus Status { get; private set; }
    public CaregiverAvailability Availability { get; private set; }
    public CaregiverPricing? Pricing { get; private set; }
    public CaregiverSchedule Schedule { get; private set; } = CaregiverSchedule.Create();
    public decimal AverageRating { get; private set; } = 0;
    public int ReviewsCount { get; private set; } = 0;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    private readonly List<CaregiverCertificate> _certificates = [];
    public IReadOnlyCollection<CaregiverCertificate> Certificates => _certificates.AsReadOnly();

    public static Caregiver Create(
        UserId userId,
        CaregiverType type)
    {
        return new Caregiver(
            CaregiverId.New(),
            userId,
            type);
    }

    public void UpdateProfile(
    string bio,
    int yearsOfExperience)
    {
        Profile = CaregiverProfile.Create(
            bio,
            yearsOfExperience);

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status = CaregiverStatus.Active;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Suspend()
    {
        Status = CaregiverStatus.Suspended;
        UpdatedOnUtc = DateTime.UtcNow;
    }
    public void BecomeAvailable()
    {
        Availability = CaregiverAvailability.Available;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void BecomeUnavailable()
    {
        Availability = CaregiverAvailability.Unavailable;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdatePricing(CaregiverPricing pricing)
    {
        Pricing = pricing;

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void AddCertificate(
        string name,
        string filePath,
        DateOnly? expiryDate = null)
    {
        _certificates.Add(
            CaregiverCertificate.Create(
                name,
                filePath,
                expiryDate));

        UpdatedOnUtc = DateTime.UtcNow;
    }
}