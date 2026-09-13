using MediatR;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Identity.Application.Abstractions.Families;

namespace Sanad.API.IdentityIntegration;

public sealed class FamilyAccountGateway : IFamilyAccountGateway
{
    private readonly ISender _sender;

    public FamilyAccountGateway(ISender sender)
    {
        _sender = sender;
    }

    public async Task<IReadOnlyList<FamilyMembership>> GetActiveFamilyRolesAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<FamilyMembershipResponse>> result =
            await _sender.Send(
                new GetActiveFamilyRolesQuery(userId),
                cancellationToken);

        if (result.IsFailure)
        {
            return [];
        }

        return result.Value
            .Select(m => new FamilyMembership(
                m.FamilyId,
                (Sanad.Modules.Identity.Application.Abstractions.Families.FamilyRole)(int)m.Role))
            .ToList();
    }

    public async Task<bool> IsElderlyDependentAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        Result<bool> result = await _sender.Send(
            new IsElderlyDependentQuery(userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return false;
        }

        return result.Value;
    }

    public async Task<Result> LeaveFamiliesForSelfDeletionAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        return await _sender.Send(
            new LeaveFamiliesForSelfDeletionCommand(userId),
            cancellationToken);
    }
}
