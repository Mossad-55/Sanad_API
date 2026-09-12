using FluentValidation;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

public sealed record LeaveFamilyCommand(
    UserId CallerUserId,
    UserId? TransferToUserId)
    : ICommand;

public sealed class LeaveFamilyCommandValidator
    : AbstractValidator<LeaveFamilyCommand>
{
    public LeaveFamilyCommandValidator()
    {
        RuleFor(c => c.CallerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.TransferToUserId)
            .NotEqual(UserId.Empty)
            .When(c => c.TransferToUserId.HasValue);
    }
}

public sealed class LeaveFamilyCommandHandler
    : ICommandHandler<LeaveFamilyCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public LeaveFamilyCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        LeaveFamilyCommand request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(
            _dbContext,
            request.CallerUserId,
            cancellationToken);

        if (family is null)
        {
            return Result.Failure(FamilyErrors.NotFound);
        }

        FamilyRole? role = family.GetRole(request.CallerUserId);

        if (role is null)
        {
            return Result.Failure(FamilyErrors.AccessDenied);
        }

        if (role == FamilyRole.Owner)
        {
            if (request.TransferToUserId is null ||
                request.TransferToUserId == UserId.Empty ||
                request.TransferToUserId == request.CallerUserId)
            {
                return Result.Failure(FamilyErrors.OwnerProtected);
            }

            if (family.GetRole(request.TransferToUserId.Value) is null)
            {
                return Result.Failure(FamilyErrors.MemberNotFound);
            }

            try
            {
                family.TransferOwnership(request.TransferToUserId.Value);
            }
            catch (DomainException exception)
            {
                return Result.Failure(exception.Message == "The family member was not found."
                    ? FamilyErrors.MemberNotFound
                    : FamilyErrors.OwnerProtected);
            }
        }

        family.RemoveMember(request.CallerUserId);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}