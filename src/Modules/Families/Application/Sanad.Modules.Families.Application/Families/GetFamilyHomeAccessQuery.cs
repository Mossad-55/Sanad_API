using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

public sealed record GetFamilyHomeAccessQuery(Guid UserId) : IQuery<Guid>;

public sealed class GetFamilyHomeAccessQueryHandler(IFamiliesDbContext dbContext)
    : IQueryHandler<GetFamilyHomeAccessQuery, Guid>
{
    public async Task<Result<Guid>> Handle(
        GetFamilyHomeAccessQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return FamilyErrors.NotFound;

        UserId userId = new(request.UserId);

        Family? family = await FamilyAccess.ResolveFamilyAsync(dbContext, userId, cancellationToken);
        if (family is null || !FamilyAccess.IsMember(family, userId))
            return FamilyErrors.NotFound;

        return Result<Guid>.Success(family.Id.Value);
    }
}