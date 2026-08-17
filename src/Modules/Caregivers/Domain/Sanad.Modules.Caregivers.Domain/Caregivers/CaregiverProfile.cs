using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverProfile : ValueObject
{
    private CaregiverProfile()
    {
    }

    private CaregiverProfile(
        string bio,
        int yearsOfExperience)
    {
        Bio = bio;
        YearsOfExperience = yearsOfExperience;
    }

    public string Bio { get; private set; } = string.Empty;

    public int YearsOfExperience { get; private set; }

    public static CaregiverProfile Create(
        string bio,
        int yearsOfExperience)
    {
        bio = bio.Trim();

        if (bio.Length > 2000)
        {
            throw new DomainException("Bio is too long.");
        }

        if (yearsOfExperience < 0)
        {
            throw new DomainException("Years of experience cannot be negative.");
        }

        return new CaregiverProfile(
            bio,
            yearsOfExperience);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Bio;
        yield return YearsOfExperience;
    }
}