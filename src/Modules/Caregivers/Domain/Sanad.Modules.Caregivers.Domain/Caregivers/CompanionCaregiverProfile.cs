using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CompanionCaregiverProfile :
    ValueObject
{
    public const int MaximumBiographyLength = 2000;

    private CompanionCaregiverProfile()
    {
    }

    private CompanionCaregiverProfile(
        int yearsOfExperience,
        SpecializationId specializationId,
        string? biography)
    {
        YearsOfExperience = yearsOfExperience;
        SpecializationId = specializationId;
        Biography = biography;
    }

    public int YearsOfExperience { get; private set; }

    public SpecializationId SpecializationId
    {
        get;
        private set;
    }

    public string? Biography { get; private set; }

    internal static CompanionCaregiverProfile Create(
        int yearsOfExperience,
        SpecializationId specializationId,
        string? biography)
    {
        if (yearsOfExperience < 0)
        {
            throw new DomainException(
                "Years of experience cannot be negative.");
        }

        if (specializationId ==
            SpecializationId.Empty)
        {
            throw new DomainException(
                "Specialization ID is required.");
        }

        string? normalizedBiography =
            NormalizeOptionalBiography(
                biography);

        return new CompanionCaregiverProfile(
            yearsOfExperience,
            specializationId,
            normalizedBiography);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return YearsOfExperience;
        yield return SpecializationId;
        yield return Biography;
    }

    private static string? NormalizeOptionalBiography(
        string? biography)
    {
        if (string.IsNullOrWhiteSpace(
            biography))
        {
            return null;
        }

        string normalizedBiography =
            biography.Trim();

        if (normalizedBiography.Length >
            MaximumBiographyLength)
        {
            throw new DomainException(
                $"Biography cannot exceed " +
                $"{MaximumBiographyLength} characters.");
        }

        return normalizedBiography;
    }
}