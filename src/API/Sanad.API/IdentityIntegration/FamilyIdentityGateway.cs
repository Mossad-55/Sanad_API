using MediatR;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.IdentityIntegration;

public sealed class FamilyIdentityGateway : IFamilyIdentityGateway
{
    private readonly ISender _sender;

    public FamilyIdentityGateway(ISender sender)
    {
        _sender = sender;
    }

    public async Task<Result<ElderlyIdentityAccount>> GetByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new GetElderlyUserIdByPhoneQuery(phoneNumber),
                cancellationToken);

        if (result.IsFailure)
        {
            return Result<ElderlyIdentityAccount>.Failure(
                result.Error);
        }

        return new ElderlyIdentityAccount(
            result.Value.UserId,
            Exists: true);
    }

    public async Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
        string arabicFullName,
        string englishFullName,
        string phoneNumber,
        Gender gender,
        DateOnly dateOfBirth,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new CreateElderlyIdentityCommand(
                    arabicFullName,
                    englishFullName,
                    phoneNumber,
                    gender,
                    dateOfBirth,
                    utcNow),
                cancellationToken);

        if (result.IsFailure)
        {
            return Result<ElderlyIdentityAccount>.Failure(
                result.Error);
        }

        return new ElderlyIdentityAccount(
            result.Value.UserId,
            Exists: true);
    }

    public async Task DeleteAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        // Best-effort compensation; result intentionally ignored.
        await _sender.Send(
            new DeleteIdentityUserCommand(userId),
            cancellationToken);
    }
}