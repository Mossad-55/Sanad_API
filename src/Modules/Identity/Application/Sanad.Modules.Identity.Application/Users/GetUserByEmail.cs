using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record FamilyEmailUser(
    UserId UserId,
    bool IsFamily);

public sealed record GetUserByEmailQuery(
    string Email)
    : IQuery<FamilyEmailUser>;

public sealed class GetUserByEmailQueryValidator
    : AbstractValidator<GetUserByEmailQuery>
{
    public GetUserByEmailQueryValidator()
    {
        RuleFor(q => q.Email).NotEmpty();
    }
}

public sealed class GetUserByEmailQueryHandler
    : IQueryHandler<GetUserByEmailQuery, FamilyEmailUser>
{
    private readonly IIdentityDbContext _dbContext;

    public GetUserByEmailQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<FamilyEmailUser>> Handle(
        GetUserByEmailQuery request,
        CancellationToken cancellationToken)
    {
        Email email = Email.Create(request.Email);

        User? user =
            await _dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    u => u.Email == email,
                    cancellationToken);

        if (user is null)
        {
            return Result<FamilyEmailUser>.Failure(
                UserLookupErrors.EmailNotFound);
        }

        bool isFamily =
            user.Accounts.Any(
                account =>
                    account.AccountType ==
                        AccountType.Family);

        return new FamilyEmailUser(user.Id, isFamily);
    }
}