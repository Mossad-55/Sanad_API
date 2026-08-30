using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

public static class OnboardingErrors
{
    public static readonly Error AlreadyExists =
        new(
            "Caregivers.Onboarding.AlreadyExists",
            "A caregiver profile already exists for this user.");

    public static readonly Error NotFound =
        new(
            "Caregivers.Onboarding.NotFound",
            "The caregiver profile was not found.");

    public static readonly Error WrongCaregiverType =
        new(
            "Caregivers.Onboarding.WrongCaregiverType",
            "This action does not match the caregiver type.");

    public static readonly Error InactiveLookup =
        new(
            "Caregivers.Onboarding.InactiveLookup",
            "One of the referenced lookups is inactive.");

    public static readonly Error InvalidSchedule =
        new(
            "Caregivers.Onboarding.InvalidSchedule",
            "The weekly schedule is invalid.");

    public static readonly Error NotActive =
        new(
            "Caregivers.Onboarding.NotActive",
            "Only an Active caregiver can perform this action.");

    public static readonly Error CertificateNotFound =
        new(
            "Caregivers.Onboarding.CertificateNotFound",
            "The certificate was not found.");

    public static readonly Error InvalidCertificateOperation =
        new(
            "Caregivers.Onboarding.InvalidCertificateOperation",
            "The certificate operation is not allowed for its current state.");
}