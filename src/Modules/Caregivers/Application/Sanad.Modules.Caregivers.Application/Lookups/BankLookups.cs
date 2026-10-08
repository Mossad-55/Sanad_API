using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record BankResponse(
    BankId Id,
    string Code,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record BankPublicItem(
    BankId Id,
    string Code,
    string ArabicName,
    string EnglishName);

internal static class BankMappings
{
    public static BankResponse ToResponse(
        this Bank bank)
    {
        return new BankResponse(
            bank.Id,
            bank.Code,
            bank.ArabicName,
            bank.EnglishName,
            bank.IsActive);
    }

    public static BankPublicItem ToPublicItem(
        this Bank bank)
    {
        return new BankPublicItem(
            bank.Id,
            bank.Code,
            bank.ArabicName,
            bank.EnglishName);
    }
}

public sealed record CreateBankCommand(
    string Code,
    string ArabicName,
    string EnglishName)
    : ICommand<BankResponse>;

public sealed class CreateBankCommandValidator :
    AbstractValidator<CreateBankCommand>
{
    public CreateBankCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .MinimumLength(Bank.MinimumCodeLength)
            .MaximumLength(Bank.MaximumCodeLength)
            .Matches("^[A-Za-z0-9]{3,11}$");

        RuleFor(command => command.ArabicName)
            .NotEmpty()
            .MaximumLength(Bank.MaximumNameLength);

        RuleFor(command => command.EnglishName)
            .NotEmpty()
            .MaximumLength(Bank.MaximumNameLength);
    }
}

public sealed class CreateBankCommandHandler :
    ICommandHandler<CreateBankCommand, BankResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateBankCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BankResponse>> Handle(
        CreateBankCommand request,
        CancellationToken cancellationToken)
    {
        string code = request.Code.Trim().ToUpperInvariant();

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicateCode =
            await _dbContext.Banks.AnyAsync(
                bank => bank.Code == code,
                cancellationToken);

        if (duplicateCode)
        {
            return LookupsErrors.BankCodeInUse;
        }

        bool duplicateName =
            await _dbContext.Banks.AnyAsync(
                bank =>
                    bank.ArabicName == arabicName ||
                    bank.EnglishName == englishName,
                cancellationToken);

        if (duplicateName)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        Bank bank =
            Bank.Create(
                request.Code,
                request.ArabicName,
                request.EnglishName);

        _dbContext.Banks.Add(bank);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return bank.ToResponse();
    }
}

public sealed record RenameBankCommand(
    BankId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<BankResponse>;

public sealed class RenameBankCommandValidator :
    AbstractValidator<RenameBankCommand>
{
    public RenameBankCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(BankId.Empty);

        RuleFor(command => command.ArabicName)
            .NotEmpty()
            .MaximumLength(Bank.MaximumNameLength);

        RuleFor(command => command.EnglishName)
            .NotEmpty()
            .MaximumLength(Bank.MaximumNameLength);
    }
}

public sealed class RenameBankCommandHandler :
    ICommandHandler<RenameBankCommand, BankResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameBankCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BankResponse>> Handle(
        RenameBankCommand request,
        CancellationToken cancellationToken)
    {
        Bank? bank =
            await _dbContext.Banks.SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);

        if (bank is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Banks.AnyAsync(
                item =>
                    item.Id != request.Id &&
                    (item.ArabicName == arabicName ||
                     item.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        bank.UpdateNames(
            request.ArabicName,
            request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return bank.ToResponse();
    }
}

public sealed record SetBankActiveCommand(
    BankId Id,
    bool IsActive)
    : ICommand<BankResponse>;

public sealed class SetBankActiveCommandValidator :
    AbstractValidator<SetBankActiveCommand>
{
    public SetBankActiveCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(BankId.Empty);
    }
}

public sealed class SetBankActiveCommandHandler :
    ICommandHandler<SetBankActiveCommand, BankResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetBankActiveCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BankResponse>> Handle(
        SetBankActiveCommand request,
        CancellationToken cancellationToken)
    {
        Bank? bank =
            await _dbContext.Banks.SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);

        if (bank is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            bank.Activate();
        }
        else
        {
            bank.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return bank.ToResponse();
    }
}

public sealed record GetActiveBanksQuery()
    : IQuery<IReadOnlyList<BankPublicItem>>;

public sealed class GetActiveBanksQueryHandler :
    IQueryHandler<
        GetActiveBanksQuery,
        IReadOnlyList<BankPublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveBanksQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BankPublicItem>>> Handle(
        GetActiveBanksQuery request,
        CancellationToken cancellationToken)
    {
        List<Bank> banks =
            await _dbContext.Banks
                .AsNoTracking()
                .Where(bank => bank.IsActive)
                .OrderBy(bank => bank.Code)
                .ToListAsync(cancellationToken);

        IReadOnlyList<BankPublicItem> items =
            banks
                .Select(bank => bank.ToPublicItem())
                .ToList();

        return Result<IReadOnlyList<BankPublicItem>>.Success(items);
    }
}

public sealed record GetAllBanksQuery()
        : IQuery<IReadOnlyList<BankResponse>>;

public sealed class GetAllBanksQueryHandler :
    IQueryHandler<GetAllBanksQuery, IReadOnlyList<BankResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllBanksQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BankResponse>>> Handle(
        GetAllBanksQuery request,
        CancellationToken cancellationToken)
    {
        List<Bank> banks =
            await _dbContext.Banks
                .AsNoTracking()
                .OrderBy(bank => bank.Code)
                .ToListAsync(cancellationToken);

        IReadOnlyList<BankResponse> items =
            banks
                .Select(bank => bank.ToResponse())
                .ToList();

        return Result<IReadOnlyList<BankResponse>>.Success(items);
    }
}
