using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record FamilyInviteeLookup(
    UserId UserId,
    bool HasFamilyAccount);

public sealed record GetFamilyInviteeByEmailQuery(
    string Email)
    : IQuery<FamilyInviteeLookup>;

public sealed class GetFamilyInviteeByEmailQueryValidator
    : AbstractValidator<GetFamilyInviteeByEmailQuery>
{
    public GetFamilyInviteeByEmailQueryValidator()
    {
        RuleFor(q => q.Email).NotEmpty();
    }
}

public sealed class GetFamilyInviteeByEmailQueryHandler
    : IQueryHandler<
        GetFamilyInviteeByEmailQuery,
        FamilyInviteeLookup>
{
    private readonly IIdentityDbContext _dbContext;

    public GetFamilyInviteeByEmailQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<FamilyInviteeLookup>> Handle(
        GetFamilyInviteeByEmailQuery request,
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
            return Result<FamilyInviteeLookup>.Failure(
                UserLookupErrors.EmailNotFound);
        }

        bool hasFamilyAccount =
            user.Accounts.Any(
                account =>
                    account.AccountType ==
                        AccountType.Family);

        return new FamilyInviteeLookup(
            user.Id,
            hasFamilyAccount);
    }
}