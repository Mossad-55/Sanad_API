using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

public sealed record FamilyMembershipResponse(
    FamilyId FamilyId,
    FamilyRole Role);

public sealed record GetActiveFamilyRolesQuery(
    UserId UserId)
    : IQuery<IReadOnlyList<FamilyMembershipResponse>>;

public sealed class GetActiveFamilyRolesQueryValidator
    : AbstractValidator<GetActiveFamilyRolesQuery>
{
    public GetActiveFamilyRolesQueryValidator()
    {
        RuleFor(q => q.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class GetActiveFamilyRolesQueryHandler
    : IQueryHandler<GetActiveFamilyRolesQuery, IReadOnlyList<FamilyMembershipResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetActiveFamilyRolesQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<FamilyMembershipResponse>>> Handle(
        GetActiveFamilyRolesQuery request,
        CancellationToken cancellationToken)
    {
        var families = await _dbContext.Families
            .Where(f => f.DeletedOnUtc == null &&
                        (f.OwnerUserId == request.UserId ||
                         f.Members.Any(m => m.Id == request.UserId)))
            .ToListAsync(cancellationToken);

        var result = new List<FamilyMembershipResponse>();

        foreach (var family in families)
        {
            var role = family.GetRole(request.UserId);
            if (role is null)
            {
                continue;
            }

            result.Add(new FamilyMembershipResponse(family.Id, role.Value));
        }

        return Result<IReadOnlyList<FamilyMembershipResponse>>.Success(result);
    }
}

public sealed record IsElderlyDependentQuery(
    UserId UserId)
    : IQuery<bool>;

public sealed class IsElderlyDependentQueryValidator
    : AbstractValidator<IsElderlyDependentQuery>
{
    public IsElderlyDependentQueryValidator()
    {
        RuleFor(q => q.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class IsElderlyDependentQueryHandler
    : IQueryHandler<IsElderlyDependentQuery, bool>
{
    private readonly IFamiliesDbContext _dbContext;

    public IsElderlyDependentQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(
        IsElderlyDependentQuery request,
        CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.Elderlies
            .AnyAsync(
                e => e.IdentityUserId == request.UserId,
                cancellationToken);

        return Result<bool>.Success(exists);
    }
}

public sealed record LeaveFamiliesForSelfDeletionCommand(
    UserId UserId)
    : ICommand;

public sealed class LeaveFamiliesForSelfDeletionCommandValidator
    : AbstractValidator<LeaveFamiliesForSelfDeletionCommand>
{
    public LeaveFamiliesForSelfDeletionCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class LeaveFamiliesForSelfDeletionCommandHandler
    : ICommandHandler<LeaveFamiliesForSelfDeletionCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public LeaveFamiliesForSelfDeletionCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        LeaveFamiliesForSelfDeletionCommand request,
        CancellationToken cancellationToken)
    {
        var families = await _dbContext.Families
            .Where(f => f.DeletedOnUtc == null &&
                        (f.OwnerUserId == request.UserId ||
                         f.Members.Any(m => m.Id == request.UserId)))
            .ToListAsync(cancellationToken);

        // Guard: if the user is OWNER of any active family → fail WITHOUT changes
        bool isOwnerOfAny = families.Any(f => f.OwnerUserId == request.UserId);

        if (isOwnerOfAny)
        {
            return Result.Failure(FamilyErrors.OwnerProtected);
        }

        foreach (var family in families)
        {
            // Remove member if present and not owner (owner case already blocked)
            if (family.GetRole(request.UserId) is not null &&
                family.OwnerUserId != request.UserId)
            {
                family.RemoveMember(request.UserId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
