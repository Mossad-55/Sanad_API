using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Abstractions.Caregivers;

public sealed record CaregiverAccountInfo(
    CaregiverId CaregiverId,
    bool IsDeactivated);

/// <summary>
/// Outbound port implemented by Ahlena.API over MediatR. Lets the Identity
/// module read and deactivate the caregiver side of an account (SET-8d)
/// without referencing the Caregivers or Families modules directly.
/// </summary>
public interface ICaregiverAccountGateway
{
    /// <summary>
    /// Returns null when the user has no caregiver profile at all.
    /// </summary>
    Task<CaregiverAccountInfo?> GetCaregiverAccountAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// D11 guard: true while any booking of this caregiver is
    /// PendingCaregiverApproval, Confirmed or InProgress. PendingPayment
    /// does not block (the slot is not committed yet).
    /// </summary>
    Task<Result<bool>> HasActiveBookingsAsync(
        CaregiverId caregiverId,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateCaregiverAsync(
        CaregiverId caregiverId,
        string reason,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
