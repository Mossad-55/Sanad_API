using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Abstractions.Security;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.PayoutAccounts;

public static class PayoutAccountErrors
{
    public static readonly Error CaregiverNotFound =
        new(
            "Caregivers.NotFound",
            "Caregiver not found.");

    public static readonly Error NotFound =
        new(
            "Caregivers.PayoutAccount.NotFound",
            "No payout account was saved for this caregiver.");

    public static readonly Error InvalidIban =
        new(
            "Caregivers.PayoutAccount.InvalidIban",
            "The IBAN is invalid.");

    public static readonly Error UnknownBank =
        new(
            "Caregivers.PayoutAccount.UnknownBank",
            "The bank code is unknown.");

    public static readonly Error InactiveBank =
        new(
            "Caregivers.PayoutAccount.InactiveBank",
            "The bank is not active.");

    public static readonly Error ProtectionUnavailable =
        new(
            "Caregivers.PayoutAccount.ProtectionUnavailable",
            "Payout account protection is temporarily unavailable.");

    public static readonly Error InvalidState =
        new(
            "Caregivers.PayoutAccount.InvalidState",
            "The payout account is not in a valid state for this operation.");

    public static readonly Error RevisionConflict =
        new(
            "Caregivers.PayoutAccount.RevisionConflict",
            "The payout account changed; reload it before deciding.");
}

public sealed record CaregiverPayoutAccountResponse(
    Guid CaregiverId,
    string AccountHolderName,
    string BankCode,
    string MaskedIban,
    string Status,
    DateTime UpdatedOnUtc);

internal static class PayoutAccountMappings
{
    public static CaregiverPayoutAccountResponse ToResponse(
        this CaregiverPayoutAccount account)
    {
        return new CaregiverPayoutAccountResponse(
            account.CaregiverId.Value,
            account.AccountHolderName,
            account.BankCode,
            account.MaskedIban(),
            account.Status.ToString(),
            account.UpdatedOnUtc);
    }
}

public sealed record GetCaregiverPayoutAccountQuery(
    Guid CaregiverId,
    UserId ActorUserId) : IQuery<CaregiverPayoutAccountResponse>;

public sealed class GetCaregiverPayoutAccountQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetCaregiverPayoutAccountQuery,
    CaregiverPayoutAccountResponse>
{
    public async Task<Result<CaregiverPayoutAccountResponse>> Handle(
        GetCaregiverPayoutAccountQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId) &&
                     c.UserId == request.ActorUserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.CaregiverNotFound);
        }

        var account = await dbContext.PayoutAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);

        if (account is null)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        return Result<CaregiverPayoutAccountResponse>.Success(
            account.ToResponse());
    }
}

public sealed record UpdateCaregiverPayoutAccountCommand(
    Guid CaregiverId,
    UserId ActorUserId,
    string AccountHolderName,
    string BankCode,
    string Iban) : ICommand<CaregiverPayoutAccountResponse>;

public sealed class UpdateCaregiverPayoutAccountCommandValidator :
    AbstractValidator<UpdateCaregiverPayoutAccountCommand>
{
    public UpdateCaregiverPayoutAccountCommandValidator()
    {
        RuleFor(command => command.AccountHolderName)
            .NotEmpty()
            .MaximumLength(CaregiverPayoutAccount.MaximumHolderNameLength);

        RuleFor(command => command.BankCode)
            .NotEmpty()
            .MaximumLength(CaregiverPayoutAccount.MaximumBankCodeLength);

        RuleFor(command => command.Iban)
            .NotEmpty()
            .MaximumLength(CaregiverPayoutAccount.MaximumIbanLength);
    }
}

public sealed class UpdateCaregiverPayoutAccountCommandHandler :
    ICommandHandler<
        UpdateCaregiverPayoutAccountCommand,
        CaregiverPayoutAccountResponse>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IIbanProtector _ibanProtector;

    public UpdateCaregiverPayoutAccountCommandHandler(
        ICaregiversDbContext dbContext,
        IIbanProtector ibanProtector)
    {
        _dbContext = dbContext;
        _ibanProtector = ibanProtector;
    }

    public async Task<Result<CaregiverPayoutAccountResponse>> Handle(
        UpdateCaregiverPayoutAccountCommand request,
        CancellationToken cancellationToken)
    {
        var caregiver = await _dbContext.Caregivers
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId) &&
                     c.UserId == request.ActorUserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.CaregiverNotFound);
        }

        string bankCode =
            request.BankCode.Trim().ToUpperInvariant();

        var bank = await _dbContext.Banks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.Code == bankCode,
                cancellationToken);

        if (bank is null)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.UnknownBank);
        }

        if (!bank.IsActive)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.InactiveBank);
        }

        var account = await _dbContext.PayoutAccounts
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);

        if (!IbanValidation.TryNormalizeIban(
                request.Iban,
                out string normalizedIban,
                out _))
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.InvalidIban);
        }

        string envelope;
        try
        {
            envelope = _ibanProtector.Protect(normalizedIban);
        }
        catch (IbanProtectionException)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.ProtectionUnavailable);
        }

        string ibanLast4 = normalizedIban[^4..];

        if (account is null)
        {
            account = CaregiverPayoutAccount.Create(
                caregiver.Id,
                request.AccountHolderName,
                request.BankCode,
                envelope,
                ibanLast4);

            _dbContext.PayoutAccounts.Add(account);
        }
        else
        {
            account.UpdateDetails(
                request.AccountHolderName,
                request.BankCode,
                envelope,
                ibanLast4);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CaregiverPayoutAccountResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        return Result<CaregiverPayoutAccountResponse>.Success(
            account.ToResponse());
    }
}
