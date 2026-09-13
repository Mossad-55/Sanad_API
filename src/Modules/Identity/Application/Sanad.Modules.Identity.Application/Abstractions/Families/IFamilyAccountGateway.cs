using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Abstractions.Families;

public enum FamilyRole
{
    Owner = 1,
    Editor = 2,
    Viewer = 3
}

public sealed record FamilyMembership(
    FamilyId FamilyId,
    FamilyRole Role);

public interface IFamilyAccountGateway
{
    Task<IReadOnlyList<FamilyMembership>> GetActiveFamilyRolesAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsElderlyDependentAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<Result> LeaveFamiliesForSelfDeletionAsync(
        UserId userId,
        CancellationToken cancellationToken = default);
}
