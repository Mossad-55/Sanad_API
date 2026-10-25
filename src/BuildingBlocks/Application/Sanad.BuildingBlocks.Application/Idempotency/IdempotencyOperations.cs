namespace Sanad.BuildingBlocks.Application.Idempotency;

/// <summary>
/// The stable operation names that scope an idempotency lookup together with
/// the authenticated actor and the caller-supplied key. Only the four
/// approved create routes publish one; every other route stays keyless.
/// </summary>
public static class IdempotencyOperations
{
    public const string FamilyBookingCheckout = "family.bookings.checkout";

    public const string FamilyCareHomeBookingCheckout = "family.care-home-bookings.checkout";

    public const string FamilyCareRecordCreate = "family.care-records.create";

    public const string CaregiverCareRecordCreate = "caregiver.care-records.create";
}
