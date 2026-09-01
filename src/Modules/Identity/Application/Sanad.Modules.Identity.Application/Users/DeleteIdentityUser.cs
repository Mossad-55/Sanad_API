using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record DeleteIdentityUserCommand(
    UserId UserId)
    : ICommand;

public sealed class DeleteIdentityUserCommandValidator
    : AbstractValidator<DeleteIdentityUserCommand>
{
    public DeleteIdentityUserCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class DeleteIdentityUserCommandHandler
    : ICommandHandler<DeleteIdentityUserCommand>
{
    private readonly IIdentityDbContext _dbContext;

    public DeleteIdentityUserCommandHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        DeleteIdentityUserCommand request,
        CancellationToken cancellationToken)
    {
        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    u => u.Id == request.UserId,
                    cancellationToken);

        // Compensation must be idempotent: nothing to undo if absent.
        if (user is null)
        {
            return Result.Success();
        }

        // Safety: this command is only ever used to roll back a
        // freshly-created elderly identity.
        bool isElderlyOnly =
            user.Accounts.Count == 1 &&
            user.Accounts.Single().AccountType ==
                AccountType.Elderly;

        if (!isElderlyOnly)
        {
            return Result.Failure(
                ElderlyIdentityErrors.PhoneAlreadyInUse);
        }

        _dbContext.Users.Remove(user);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}