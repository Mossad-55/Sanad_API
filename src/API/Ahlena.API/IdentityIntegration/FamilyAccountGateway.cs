using MediatR;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Elderlies;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Identity.Application.Abstractions.Families;

namespace Ahlena.API.IdentityIntegration;

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
                (Ahlena.Modules.Identity.Application.Abstractions.Families.FamilyRole)(int)m.Role))
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

    public async Task<bool> HasUsableElderlyProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        Result<bool> result = await _sender.Send(
            new HasUsableElderlyProfileQuery(userId),
            cancellationToken);

        return result.IsSuccess && result.Value;
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
