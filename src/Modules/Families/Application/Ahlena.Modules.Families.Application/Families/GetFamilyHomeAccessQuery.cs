using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Families;

namespace Ahlena.Modules.Families.Application.Families;

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