using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Abstractions.Identity;

public sealed record ElderlyIdentityAccount(
    UserId UserId,
    bool Exists,
    bool IsElderly);

public sealed record FamilyInviteeAccount(
    UserId UserId,
    bool Exists,
    bool HasFamilyAccount);

/// <summary>
/// Outbound port implemented by Sanad.API over MediatR. Lets the Families
/// module interact with the Identity module without referencing it
/// directly: elderly login provisioning (F2) and family-member invitation
/// lookup/email (F3).
/// </summary>
public interface IFamilyIdentityGateway
{
    Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken = default);

    Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
        string arabicFullName,
        string englishFullName,
        string phoneNumber,
        Gender gender,
        DateOnly dateOfBirth,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task DeleteElderlyAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort delivery of the invitation deep link. The invitation is
    /// persisted before this is called; a mail failure must not roll it back.
    /// </summary>
    Task SendFamilyInvitationEmailAsync(
        string email,
        string familyName,
        string invitationToken,
        CancellationToken cancellationToken = default);
}