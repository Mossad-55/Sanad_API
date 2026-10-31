using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Caregivers.Application.Onboarding;

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

    /// <summary>
    /// The named certificate version does not exist on this caregiver's
    /// qualification. Also returned when the caller is not authorized for the
    /// certificate's scope, so existence is never leaked across caregivers.
    /// </summary>
    public static readonly Error CertificateVersionNotFound =
        new(
            "Caregivers.Onboarding.CertificateVersionNotFound",
            "The certificate version was not found.");

    /// <summary>
    /// The version exists but is not a removable retained-history version: it
    /// is the current effective version (even when the Cairo calendar has made
    /// it expired) or the live pending review candidate.
    /// </summary>
    public static readonly Error CertificateVersionNotRemovable =
        new(
            "Caregivers.Onboarding.CertificateVersionNotRemovable",
            "Only a retained certificate history version can be removed.");

    /// <summary>
    /// The qualification changed between the client's read and the write, so
    /// the server-recomputed version classification no longer matched. The
    /// caller should re-read and retry.
    /// </summary>
    public static readonly Error CertificateConflict =
        new(
            "Caregivers.Onboarding.CertificateConflict",
            "The certificate changed since it was read. Reload and retry.");

    public static readonly Error InvalidState =
        new(
            "Caregivers.Onboarding.InvalidState",
            "The caregiver cannot perform this actiono in its current status.");

    public static readonly Error CaregiverNotFound =
        new(
            "Caregivers.Onboarding.CaregiverNotFound",
            "The caregiver was not found.");
}