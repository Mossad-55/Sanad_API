using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record AnonymizeFamilyAccountsCommand(
    IReadOnlyCollection<UserId> UserIds)
    : ICommand;

public sealed class AnonymizeFamilyAccountsCommandValidator
    : AbstractValidator<AnonymizeFamilyAccountsCommand>
{
    public AnonymizeFamilyAccountsCommandValidator()
    {
        RuleFor(c => c.UserIds).NotNull().NotEmpty();
    }
}

public sealed class AnonymizeFamilyAccountsCommandHandler
    : ICommandHandler<AnonymizeFamilyAccountsCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AnonymizeFamilyAccountsCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        AnonymizeFamilyAccountsCommand request,
        CancellationToken cancellationToken)
    {
        DateTime utcNow = _dateTimeProvider.UtcNow;

        List<User> users = await _dbContext.Users
            .Where(user => request.UserIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        if (users.Count != request.UserIds.Distinct().Count())
        {
            return Result.Failure(AccountErrors.UserNotFound);
        }

        foreach (User user in users)
        {
            bool hasCaregiverAccount = user.Accounts.Any(
                a => a.AccountType is AccountType.MedicalCaregiver
                              or AccountType.CompanionCaregiver);

            if (hasCaregiverAccount)
            {
                continue;
            }

            user.AnonymizeAndDeactivate(utcNow);

            DeviceSession[] sessions = await _dbContext.DeviceSessions
                .Where(s => s.UserId == user.Id && s.RevokedOnUtc == null)
                .ToArrayAsync(cancellationToken);

            foreach (DeviceSession session in sessions)
            {
                session.Revoke("The family account was anonymized.", utcNow);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
