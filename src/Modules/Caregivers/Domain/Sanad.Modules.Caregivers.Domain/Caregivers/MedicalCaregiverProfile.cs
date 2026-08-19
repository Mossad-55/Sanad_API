using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class MedicalCaregiverProfile : ValueObject
{
    public const int MaximumBiographyLength = 2000;
    public const int MaximumWorkplaceLength = 200;

    private MedicalCaregiverProfile()
    {
    }

    private MedicalCaregiverProfile(
        ProfessionalTitleId professionalTitleId,
        int yearsOfExperience,
        SpecializationId specializationId,
        AcademicDegreeId academicDegreeId,
        string? currentWorkplace,
        string? biography)
    {
        ProfessionalTitleId = professionalTitleId;
        YearsOfExperience = yearsOfExperience;
        SpecializationId = specializationId;
        AcademicDegreeId = academicDegreeId;
        CurrentWorkplace = currentWorkplace;
        Biography = biography;
    }

    public ProfessionalTitleId ProfessionalTitleId
    {
        get;
        private set;
    }

    public int YearsOfExperience { get; private set; }

    public SpecializationId SpecializationId
    {
        get;
        private set;
    }

    public AcademicDegreeId AcademicDegreeId
    {
        get;
        private set;
    }

    public string? CurrentWorkplace { get; private set; }

    public string? Biography { get; private set; }

    internal static MedicalCaregiverProfile Create(
        ProfessionalTitleId professionalTitleId,
        int yearsOfExperience,
        SpecializationId specializationId,
        AcademicDegreeId academicDegreeId,
        string? currentWorkplace,
        string? biography)
    {
        if (professionalTitleId ==
            ProfessionalTitleId.Empty)
        {
            throw new DomainException(
                "Professional Title ID is required.");
        }

        if (specializationId ==
            SpecializationId.Empty)
        {
            throw new DomainException(
                "Specialization ID is required.");
        }

        if (academicDegreeId ==
            AcademicDegreeId.Empty)
        {
            throw new DomainException(
                "Academic Degree ID is required.");
        }

        if (yearsOfExperience < 0)
        {
            throw new DomainException(
                "Years of experience cannot be negative.");
        }

        string? normalizedWorkplace =
            NormalizeOptionalText(
                currentWorkplace,
                MaximumWorkplaceLength,
                "Current workplace");

        string? normalizedBiography =
            NormalizeOptionalText(
                biography,
                MaximumBiographyLength,
                "Biography");

        return new MedicalCaregiverProfile(
            professionalTitleId,
            yearsOfExperience,
            specializationId,
            academicDegreeId,
            normalizedWorkplace,
            normalizedBiography);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return ProfessionalTitleId;
        yield return YearsOfExperience;
        yield return SpecializationId;
        yield return AcademicDegreeId;
        yield return CurrentWorkplace;
        yield return Biography;
    }

    private static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalizedValue =
            value.Trim();

        if (normalizedValue.Length >
            maximumLength)
        {
            throw new DomainException(
                $"{fieldName} cannot exceed " +
                $"{maximumLength} characters.");
        }

        return normalizedValue;
    }
}