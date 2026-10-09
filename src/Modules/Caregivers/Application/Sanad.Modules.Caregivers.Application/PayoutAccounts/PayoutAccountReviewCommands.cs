using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Abstractions.Security;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.PayoutAccounts;

public sealed record PayoutAccountAdminItem(
    Guid CaregiverId,
    string ArabicFullName,
    string EnglishFullName,
    string AccountHolderName,
    string BankCode,
    string? BankName,
    string MaskedIban,
    string Status,
    int Revision,
    DateTime UpdatedOnUtc);

public sealed record PayoutAccountAdminDetailResponse(
    Guid CaregiverId,
    string ArabicFullName,
    string EnglishFullName,
    string AccountHolderName,
    string BankCode,
    string? BankName,
    string MaskedIban,
    string Status,
    int Revision,
    string? RejectionReason,
    Guid? VerifiedBy,
    DateTime? VerifiedOnUtc,
    Guid? ReviewedBy,
    DateTime? ReviewedOnUtc,
    string? VerificationSource,
    string? Reference,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record PayoutAccountReviewResponse(
    Guid Id,
    int AccountRevision,
    string Decision,
    Guid ActorUserId,
    DateTime OccurredOnUtc,
    string? VerificationSource,
    string? Reference,
    string? Reason);

public sealed record IbanRevealResponse(
    Guid CaregiverId,
    string Iban,
    string MaskedIban);

public sealed record GetPayoutAccountsQuery(
    PayoutAccountStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<PayoutAccountAdminItem>>;

public sealed class GetPayoutAccountsQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetPayoutAccountsQuery,
    PagedResult<PayoutAccountAdminItem>>
{
    public async Task<Result<PagedResult<PayoutAccountAdminItem>>> Handle(
        GetPayoutAccountsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = dbContext.PayoutAccounts.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(a => a.Status == request.Status.Value);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        var accounts = await query
            .OrderBy(a => a.Status == PayoutAccountStatus.Pending ? 0 : 1)
            .ThenBy(a => a.UpdatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<PayoutAccountAdminItem>(accounts.Count);
        foreach (var account in accounts)
        {
            items.Add(await MapItemAsync(dbContext, account, cancellationToken));
        }

        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<PagedResult<PayoutAccountAdminItem>>.Success(
            new PagedResult<PayoutAccountAdminItem>(items, page, pageSize, totalCount, totalPages));
    }

    internal static async Task<PayoutAccountAdminItem> MapItemAsync(
        ICaregiversDbContext dbContext,
        CaregiverPayoutAccount account,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == account.CaregiverId, cancellationToken);

        string arabicFullName = string.Empty;
        string englishFullName = string.Empty;
        if (caregiver is not null)
        {
            var header = await dbContext.GetCaregiverUserHeaderAsync(
                caregiver.UserId, cancellationToken);
            if (header is not null)
            {
                arabicFullName = header.ArabicFullName;
                englishFullName = header.EnglishFullName;
            }
        }

        var bank = await dbContext.Banks
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Code == account.BankCode, cancellationToken);

        return new PayoutAccountAdminItem(
            account.CaregiverId.Value,
            arabicFullName,
            englishFullName,
            account.AccountHolderName,
            account.BankCode,
            bank?.EnglishName,
            account.MaskedIban(),
            account.Status.ToString(),
            account.Revision,
            account.UpdatedOnUtc);
    }
}

public sealed record GetPayoutAccountDetailQuery(
    Guid CaregiverId) : IQuery<PayoutAccountAdminDetailResponse>;

public sealed class GetPayoutAccountDetailQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetPayoutAccountDetailQuery,
    PayoutAccountAdminDetailResponse>
{
    public async Task<Result<PayoutAccountAdminDetailResponse>> Handle(
        GetPayoutAccountDetailQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.CaregiverNotFound);
        }

        var account = await dbContext.PayoutAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);

        if (account is null)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        var header = await dbContext.GetCaregiverUserHeaderAsync(
            caregiver.UserId, cancellationToken);

        var bank = await dbContext.Banks
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Code == account.BankCode, cancellationToken);

        return Result<PayoutAccountAdminDetailResponse>.Success(
            new PayoutAccountAdminDetailResponse(
                account.CaregiverId.Value,
                header?.ArabicFullName ?? string.Empty,
                header?.EnglishFullName ?? string.Empty,
                account.AccountHolderName,
                account.BankCode,
                bank?.EnglishName,
                account.MaskedIban(),
                account.Status.ToString(),
                account.Revision,
                account.RejectionReason,
                account.VerifiedBy?.Value,
                account.VerifiedOnUtc,
                account.ReviewedBy?.Value,
                account.ReviewedOnUtc,
                account.VerificationSource,
                account.Reference,
                account.CreatedOnUtc,
                account.UpdatedOnUtc));
    }
}

public sealed record GetPayoutAccountReviewsQuery(
    Guid CaregiverId) : IQuery<IReadOnlyList<PayoutAccountReviewResponse>>;

public sealed class GetPayoutAccountReviewsQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetPayoutAccountReviewsQuery,
    IReadOnlyList<PayoutAccountReviewResponse>>
{
    public async Task<Result<IReadOnlyList<PayoutAccountReviewResponse>>> Handle(
        GetPayoutAccountReviewsQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return Result<IReadOnlyList<PayoutAccountReviewResponse>>.Failure(
                PayoutAccountErrors.CaregiverNotFound);
        }

        var account = await dbContext.PayoutAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);

        if (account is null)
        {
            return Result<IReadOnlyList<PayoutAccountReviewResponse>>.Failure(
                PayoutAccountErrors.NotFound);
        }

        var reviews = await dbContext.PayoutAccountReviews
            .AsNoTracking()
            .Where(r => r.PayoutAccountId == account.Id)
            .OrderByDescending(r => r.OccurredOnUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayoutAccountReviewResponse>>.Success(
            reviews.Select(r => new PayoutAccountReviewResponse(
                r.Id.Value,
                r.AccountRevision,
                r.Decision.ToString(),
                r.ActorUserId.Value,
                r.OccurredOnUtc,
                r.VerificationSource,
                r.Reference,
                r.Reason)).ToList());
    }
}

public sealed record ApprovePayoutAccountCommand(
    Guid CaregiverId,
    UserId ActorUserId,
    int ExpectedRevision,
    string VerificationSource,
    string? Reference,
    DateTime UtcNow) : ICommand<PayoutAccountAdminDetailResponse>;

public sealed class ApprovePayoutAccountCommandValidator :
    AbstractValidator<ApprovePayoutAccountCommand>
{
    public ApprovePayoutAccountCommandValidator()
    {
        RuleFor(command => command.CaregiverId)
            .NotEqual(Guid.Empty);

        RuleFor(command => command.ExpectedRevision)
            .GreaterThanOrEqualTo(1);

        RuleFor(command => command.VerificationSource)
            .NotEmpty()
            .MaximumLength(CaregiverPayoutAccount.MaximumSourceLength);

        RuleFor(command => command.Reference)
            .MaximumLength(CaregiverPayoutAccount.MaximumReferenceLength)
            .When(command => command.Reference is not null);
    }
}

public sealed class ApprovePayoutAccountCommandHandler(
    ICaregiversDbContext dbContext) : ICommandHandler<
    ApprovePayoutAccountCommand,
    PayoutAccountAdminDetailResponse>
{
    public async Task<Result<PayoutAccountAdminDetailResponse>> Handle(
        ApprovePayoutAccountCommand request,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(request.CaregiverId, cancellationToken);
        if (account is null)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        if (account.Status != PayoutAccountStatus.Pending)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.InvalidState);
        }

        if (account.Revision != request.ExpectedRevision)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        try
        {
            account.Verify(
                request.ActorUserId,
                request.ExpectedRevision,
                request.UtcNow,
                request.VerificationSource,
                request.Reference);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.InvalidState);
        }

        dbContext.PayoutAccountReviews.Add(
            CaregiverPayoutAccountReview.Create(
                account.Id,
                request.ExpectedRevision,
                PayoutAccountReviewDecision.Verified,
                request.ActorUserId,
                request.UtcNow,
                account.VerificationSource,
                account.Reference));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        return await new GetPayoutAccountDetailQueryHandler(dbContext).Handle(
            new GetPayoutAccountDetailQuery(request.CaregiverId),
            cancellationToken);
    }

    private async Task<CaregiverPayoutAccount?> ResolveAccountAsync(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(caregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return null;
        }

        return await dbContext.PayoutAccounts
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);
    }
}

public sealed record RejectPayoutAccountCommand(
    Guid CaregiverId,
    UserId ActorUserId,
    int ExpectedRevision,
    string Reason,
    DateTime UtcNow) : ICommand<PayoutAccountAdminDetailResponse>;

public sealed class RejectPayoutAccountCommandValidator :
    AbstractValidator<RejectPayoutAccountCommand>
{
    public RejectPayoutAccountCommandValidator()
    {
        RuleFor(command => command.CaregiverId)
            .NotEqual(Guid.Empty);

        RuleFor(command => command.ExpectedRevision)
            .GreaterThanOrEqualTo(1);

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(CaregiverPayoutAccount.MaximumReasonLength);
    }
}

public sealed class RejectPayoutAccountCommandHandler(
    ICaregiversDbContext dbContext) : ICommandHandler<
    RejectPayoutAccountCommand,
    PayoutAccountAdminDetailResponse>
{
    public async Task<Result<PayoutAccountAdminDetailResponse>> Handle(
        RejectPayoutAccountCommand request,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(request.CaregiverId, cancellationToken);
        if (account is null)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        if (account.Status != PayoutAccountStatus.Pending)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.InvalidState);
        }

        if (account.Revision != request.ExpectedRevision)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        try
        {
            account.Reject(
                request.ActorUserId,
                request.Reason,
                request.ExpectedRevision,
                request.UtcNow);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.InvalidState);
        }

        dbContext.PayoutAccountReviews.Add(
            CaregiverPayoutAccountReview.Create(
                account.Id,
                request.ExpectedRevision,
                PayoutAccountReviewDecision.Rejected,
                request.ActorUserId,
                request.UtcNow,
                reason: account.RejectionReason));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        return await new GetPayoutAccountDetailQueryHandler(dbContext).Handle(
            new GetPayoutAccountDetailQuery(request.CaregiverId),
            cancellationToken);
    }

    private async Task<CaregiverPayoutAccount?> ResolveAccountAsync(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(caregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return null;
        }

        return await dbContext.PayoutAccounts
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);
    }
}

public sealed record RevokePayoutAccountCommand(
    Guid CaregiverId,
    UserId ActorUserId,
    int ExpectedRevision,
    string Reason,
    DateTime UtcNow) : ICommand<PayoutAccountAdminDetailResponse>;

public sealed class RevokePayoutAccountCommandValidator :
    AbstractValidator<RevokePayoutAccountCommand>
{
    public RevokePayoutAccountCommandValidator()
    {
        RuleFor(command => command.CaregiverId)
            .NotEqual(Guid.Empty);

        RuleFor(command => command.ExpectedRevision)
            .GreaterThanOrEqualTo(1);

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(CaregiverPayoutAccount.MaximumReasonLength);
    }
}

public sealed class RevokePayoutAccountCommandHandler(
    ICaregiversDbContext dbContext) : ICommandHandler<
    RevokePayoutAccountCommand,
    PayoutAccountAdminDetailResponse>
{
    public async Task<Result<PayoutAccountAdminDetailResponse>> Handle(
        RevokePayoutAccountCommand request,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(request.CaregiverId, cancellationToken);
        if (account is null)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        if (account.Status != PayoutAccountStatus.Verified)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.InvalidState);
        }

        if (account.Revision != request.ExpectedRevision)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        try
        {
            account.Revoke(
                request.ActorUserId,
                request.Reason,
                request.ExpectedRevision,
                request.UtcNow);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.InvalidState);
        }

        dbContext.PayoutAccountReviews.Add(
            CaregiverPayoutAccountReview.Create(
                account.Id,
                request.ExpectedRevision,
                PayoutAccountReviewDecision.Revoked,
                request.ActorUserId,
                request.UtcNow,
                reason: account.RejectionReason));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PayoutAccountAdminDetailResponse>.Failure(
                PayoutAccountErrors.RevisionConflict);
        }

        return await new GetPayoutAccountDetailQueryHandler(dbContext).Handle(
            new GetPayoutAccountDetailQuery(request.CaregiverId),
            cancellationToken);
    }

    private async Task<CaregiverPayoutAccount?> ResolveAccountAsync(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(caregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return null;
        }

        return await dbContext.PayoutAccounts
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);
    }
}

public sealed record RevealPayoutAccountIbanCommand(
    Guid CaregiverId,
    UserId ActorUserId,
    DateTime UtcNow) : ICommand<IbanRevealResponse>;

public sealed class RevealPayoutAccountIbanCommandHandler(
    ICaregiversDbContext dbContext,
    IIbanProtector ibanProtector) : ICommandHandler<
    RevealPayoutAccountIbanCommand,
    IbanRevealResponse>
{
    public async Task<Result<IbanRevealResponse>> Handle(
        RevealPayoutAccountIbanCommand request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return Result<IbanRevealResponse>.Failure(
                PayoutAccountErrors.CaregiverNotFound);
        }

        var account = await dbContext.PayoutAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                a => a.CaregiverId == caregiver.Id,
                cancellationToken);

        if (account is null)
        {
            return Result<IbanRevealResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        string iban;
        try
        {
            iban = ibanProtector.Unprotect(account.IbanCiphertext);
        }
        catch (IbanProtectionException)
        {
            return Result<IbanRevealResponse>.Failure(
                PayoutAccountErrors.ProtectionUnavailable);
        }

        dbContext.PayoutAccountReviews.Add(
            CaregiverPayoutAccountReview.Create(
                account.Id,
                account.Revision,
                PayoutAccountReviewDecision.Revealed,
                request.ActorUserId,
                request.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<IbanRevealResponse>.Success(
            new IbanRevealResponse(
                account.CaregiverId.Value,
                iban,
                account.MaskedIban()));
    }
}
