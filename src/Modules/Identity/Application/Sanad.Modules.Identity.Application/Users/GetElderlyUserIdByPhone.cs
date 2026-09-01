using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record ElderlyPhoneUser(
    UserId UserId,
    bool IsElderly);

public sealed record GetElderlyUserIdByPhoneQuery(
    string PhoneNumber)
    : IQuery<ElderlyPhoneUser>;

public sealed class GetElderlyUserIdByPhoneQueryValidator
    : AbstractValidator<GetElderlyUserIdByPhoneQuery>
{
    public GetElderlyUserIdByPhoneQueryValidator()
    {
        RuleFor(q => q.PhoneNumber).NotEmpty();
    }
}

public sealed class GetElderlyUserIdByPhoneQueryHandler
    : IQueryHandler<GetElderlyUserIdByPhoneQuery, ElderlyPhoneUser>
{
    private readonly IIdentityDbContext _dbContext;

    public GetElderlyUserIdByPhoneQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ElderlyPhoneUser>> Handle(
        GetElderlyUserIdByPhoneQuery request,
        CancellationToken cancellationToken)
    {
        PhoneNumber phone =
            PhoneNumber.Create(request.PhoneNumber);

        User? user =
            await _dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    u => u.PhoneNumber == phone,
                    cancellationToken);

        if (user is null)
        {
            return Result<ElderlyPhoneUser>.Failure(
                ElderlyIdentityErrors.ElderlyUserNotFound);
        }

        bool isElderly =
            user.Accounts.Count == 1 &&
            user.Accounts.Single().AccountType ==
                AccountType.Elderly;

        return new ElderlyPhoneUser(user.Id, isElderly);
    }
}