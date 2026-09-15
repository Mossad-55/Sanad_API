using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record AccountChoicesResponse(IReadOnlyList<AccountType> Accounts);
public sealed record AddAccountResponse(AccountType AccountType, bool RefreshRequired);
public sealed record SwitchAccountResponse(AccountType AccountType);
public sealed record GetMyAccountsQuery(UserId CurrentUserId) : IQuery<AccountChoicesResponse>;
public sealed record AddMyAccountCommand(UserId CurrentUserId, AccountType AccountType) : ICommand<AddAccountResponse>;
public sealed record SwitchMyAccountCommand(UserId CurrentUserId, AccountType AccountType) : ICommand<SwitchAccountResponse>;

public sealed class GetMyAccountsQueryValidator : AbstractValidator<GetMyAccountsQuery>
{
    public GetMyAccountsQueryValidator() => RuleFor(x => x.CurrentUserId).NotEqual(UserId.Empty);
}
public sealed class AddMyAccountCommandValidator : AbstractValidator<AddMyAccountCommand>
{
    public AddMyAccountCommandValidator()
    {
        RuleFor(x => x.CurrentUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.AccountType).IsInEnum();
    }
}
public sealed class SwitchMyAccountCommandValidator : AbstractValidator<SwitchMyAccountCommand>
{
    public SwitchMyAccountCommandValidator()
    {
        RuleFor(x => x.CurrentUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.AccountType).IsInEnum();
    }
}

internal static class AccountChoiceRules
{
    public static bool IsRegular(AccountType type) => type is
        AccountType.Family or AccountType.MedicalCaregiver or AccountType.CompanionCaregiver;
    public static bool IsCaregiver(AccountType type) => type is
        AccountType.MedicalCaregiver or AccountType.CompanionCaregiver;
    public static bool CanManage(User user) => user.Status == UserStatus.Active &&
        user.Accounts.Count > 0 && user.Accounts.All(a => IsRegular(a.AccountType));
}

public sealed class GetMyAccountsQueryHandler(IIdentityDbContext dbContext)
    : IQueryHandler<GetMyAccountsQuery, AccountChoicesResponse>
{
    public async Task<Result<AccountChoicesResponse>> Handle(GetMyAccountsQuery request, CancellationToken cancellationToken)
    {
        User? user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == request.CurrentUserId, cancellationToken);
        if (user is null) return AccountErrors.UserNotFound;
        if (!AccountChoiceRules.CanManage(user)) return AccountErrors.InvalidOperation;
        return new AccountChoicesResponse(user.Accounts.Select(a => a.AccountType).OrderBy(a => a).ToArray());
    }
}

public sealed class AddMyAccountCommandHandler(IIdentityDbContext dbContext, ICaregiverAccountGateway caregiverGateway)
    : ICommandHandler<AddMyAccountCommand, AddAccountResponse>
{
    public async Task<Result<AddAccountResponse>> Handle(AddMyAccountCommand request, CancellationToken cancellationToken)
    {
        // Repeat critical validation for direct handler callers.
        if (!AccountChoiceRules.IsRegular(request.AccountType)) return AccountErrors.UnsupportedAccountType;
        User? user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == request.CurrentUserId, cancellationToken);
        if (user is null) return AccountErrors.UserNotFound;
        if (!AccountChoiceRules.CanManage(user)) return AccountErrors.InvalidOperation;
        if (user.Accounts.Any(a => a.AccountType == request.AccountType)) return AccountErrors.AccountAlreadyExists;
        if (AccountChoiceRules.IsCaregiver(request.AccountType))
        {
            if (user.Accounts.Any(a => AccountChoiceRules.IsCaregiver(a.AccountType)))
                return AccountErrors.CaregiverTypesExclusive;
            // Never silently reactivate retained/deactivated caregiver data.
            if (await caregiverGateway.GetCaregiverAccountAsync(user.Id, cancellationToken) is not null)
                return AccountErrors.RetainedCaregiverProfile;
        }
        user.AddAccount(request.AccountType);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (AccountWriteConflictException ex)
        {
            return ex.CaregiverExclusivity ? AccountErrors.CaregiverTypesExclusive : AccountErrors.AccountAlreadyExists;
        }
        // Existing auth refresh rotates the same session and picks up all current owned claims.
        // Adding a role does not create a family/profile or bypass normal onboarding.
        return new AddAccountResponse(request.AccountType, RefreshRequired: true);
    }
}

public sealed class SwitchMyAccountCommandHandler(IIdentityDbContext dbContext)
    : ICommandHandler<SwitchMyAccountCommand, SwitchAccountResponse>
{
    public async Task<Result<SwitchAccountResponse>> Handle(SwitchMyAccountCommand request, CancellationToken cancellationToken)
    {
        if (!AccountChoiceRules.IsRegular(request.AccountType)) return AccountErrors.UnsupportedAccountType;
        User? user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == request.CurrentUserId, cancellationToken);
        if (user is null) return AccountErrors.UserNotFound;
        if (!AccountChoiceRules.CanManage(user)) return AccountErrors.InvalidOperation;
        if (!user.Accounts.Any(a => a.AccountType == request.AccountType)) return AccountErrors.AccountNotOwned;
        // Client-local dashboard selection: deliberately no write, JWT issuance, or new session.
        return new SwitchAccountResponse(request.AccountType);
    }
}
