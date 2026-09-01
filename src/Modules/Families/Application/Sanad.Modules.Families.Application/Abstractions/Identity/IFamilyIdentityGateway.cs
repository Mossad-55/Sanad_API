using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Abstractions.Identity;

public sealed record ElderlyIdentityAccount(
    UserId UserId,
    bool Exists,
    bool IsElderly);

/// <summary>
/// Outbound port implemented by Sanad.API over MediatR. Lets the Families
/// module create/lookup an elderly Identity user without referencing the
/// Identity module directly.
/// </summary>
public interface IFamilyIdentityGateway
{
    Task<Result<ElderlyIdentityAccount>> GetByPhoneAsync(
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

    Task DeleteAsync(
        UserId userId,
        CancellationToken cancellationToken = default);
}