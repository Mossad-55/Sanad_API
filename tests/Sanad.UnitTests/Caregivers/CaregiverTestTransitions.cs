using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

internal static class CaregiverTestTransitions
{
    private static readonly DateTime BaseUtc =
        new(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);

    internal static void TransitionToActive(
        this Caregiver caregiver)
    {
        switch (caregiver.Status)
        {
            case CaregiverStatus.Onboarding:
                caregiver.SubmitForReview(
                    BaseUtc);

                caregiver.Approve(
                    BaseUtc.AddMinutes(1));
                return;

            case CaregiverStatus.PendingReview:
                caregiver.Approve(
                    BaseUtc.AddMinutes(1));
                return;

            case CaregiverStatus.NeedsCorrection:
                caregiver.ResubmitForReview(
                    BaseUtc);

                caregiver.Approve(
                    BaseUtc.AddMinutes(1));
                return;

            case CaregiverStatus.Suspended:
                caregiver.Reactivate(
                    BaseUtc.AddMinutes(1));
                return;

            case CaregiverStatus.Active:
                return;

            case CaregiverStatus.Rejected:
                throw new InvalidOperationException(
                    "A Rejected caregiver cannot become " +
                    "Active in test setup.");

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(caregiver.Status),
                    caregiver.Status,
                    "Unsupported Caregiver status.");
        }
    }

    internal static void TransitionToSuspended(
        this Caregiver caregiver)
    {
        caregiver.TransitionToActive();

        caregiver.Suspend(
            "Suspended for test setup.",
            BaseUtc.AddMinutes(2));
    }
}