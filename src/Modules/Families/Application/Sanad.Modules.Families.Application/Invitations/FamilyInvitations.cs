using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;

namespace Sanad.Modules.Families.Application.Invitations;

public sealed record FamilyInvitationResponse(
    FamilyInvitationId Id,
    FamilyId FamilyId,
    string? FamilyName,
    string InvitedEmail,
    FamilyRole Role,
    FamilyRelationshipType RelationshipType,
    FamilyInvitationStatus Status,
    DateTime CreatedOnUtc,
    DateTime ExpiresOnUtc);

internal static class FamilyInvitationMappings
{
    public static FamilyInvitationResponse ToResponse(
        this FamilyInvitation invitation,
        string? familyName = null) =>
        new(
            invitation.Id,
            invitation.FamilyId,
            familyName,
            invitation.InvitedEmail,
            invitation.Role,
            invitation.RelationshipType,
            invitation.Status,
            invitation.CreatedOnUtc,
            invitation.ExpiresOnUtc);
}

// ------------------------------ Create --------------------------------

public sealed record CreateFamilyInvitationCommand(
    UserId InvitingUserId,
    string Email,
    FamilyRole Role,
    FamilyRelationshipType RelationshipType,
    DateTime UtcNow)
    : ICommand<FamilyInvitationResponse>;

public sealed class CreateFamilyInvitationCommandValidator
    : AbstractValidator<CreateFamilyInvitationCommand>
{
    public CreateFamilyInvitationCommandValidator()
    {
        RuleFor(c => c.InvitingUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();
        RuleFor(c => c.Role)
            .IsInEnum()
            .Must(role =>
                role is FamilyRole.Editor
                    or FamilyRole.Viewer)
            .WithMessage("Invited members can only be Editors or Viewers.");
        RuleFor(c => c.RelationshipType).IsInEnum();
    }
}

public sealed class CreateFamilyInvitationCommandHandler
    : ICommandHandler<
        CreateFamilyInvitationCommand,
        FamilyInvitationResponse>
{
    private const string EmailNotFoundCode =
        "Identity.User.EmailNotFound";

    private readonly IFamiliesDbContext _dbContext;
    private readonly IFamilyIdentityGateway _identityGateway;

    public CreateFamilyInvitationCommandHandler(
        IFamiliesDbContext dbContext,
        IFamilyIdentityGateway identityGateway)
    {
        _dbContext = dbContext;
        _identityGateway = identityGateway;
    }

    public async Task<Result<FamilyInvitationResponse>> Handle(
        CreateFamilyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.InvitingUserId,
                cancellationToken);

        if (family is null)
        {
            return FamilyInvitationErrors.FamilyNotFound;
        }

        // Owner or Editor only.
        if (!FamilyAccess.CanManage(family, request.InvitingUserId))
        {
            return FamilyInvitationErrors.AccessDenied;
        }

        Email email;
        try
        {
            email = Email.Create(request.Email);
        }
        catch (DomainException)
        {
            return FamilyInvitationErrors.RecipientNotRegistered;
        }

        Result<FamilyInviteeAccount> invitee =
            await _identityGateway.GetFamilyInviteeByEmailAsync(
                email.Value,
                cancellationToken);

        if (invitee.IsFailure)
        {
            return invitee.Error.Code == EmailNotFoundCode
                ? FamilyInvitationErrors.RecipientNotRegistered
                : Result<FamilyInvitationResponse>.Failure(invitee.Error);
        }

        if (!invitee.Value.HasFamilyAccount)
        {
            return FamilyInvitationErrors.RecipientMissingFamilyAccount;
        }

        if (invitee.Value.UserId == request.InvitingUserId)
        {
            return FamilyInvitationErrors.CannotInviteYourself;
        }

        if (family.GetRole(invitee.Value.UserId) is not null)
        {
            return FamilyInvitationErrors.AlreadyMember;
        }

        bool pendingExists =
            await _dbContext.Invitations.AnyAsync(
                invitation =>
                    invitation.FamilyId == family.Id &&
                    invitation.InvitedUserId == invitee.Value.UserId &&
                    invitation.Status ==
                        FamilyInvitationStatus.Pending,
                cancellationToken);

        if (pendingExists)
        {
            return FamilyInvitationErrors.PendingInvitationExists;
        }

        FamilyInvitation invitation;
        string plainToken;
        try
        {
            (invitation, plainToken) =
                FamilyInvitation.Create(
                    family.Id,
                    email,
                    invitee.Value.UserId,
                    request.Role,
                    request.RelationshipType,
                    request.InvitingUserId,
                    request.UtcNow);
        }
        catch (DomainException)
        {
            return FamilyInvitationErrors.InvalidRole;
        }

        _dbContext.Invitations.Add(invitation);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Best-effort delivery: the invitation is already persisted; a
        // mail failure must not undo it (the deep link is also printed by
        // the development mail sink for local testing).
        try
        {
            await _identityGateway.SendFamilyInvitationEmailAsync(
                email.Value,
                family.Name,
                plainToken,
                cancellationToken);
        }
        catch
        {
            // Intentionally swallowed; operational retry is out of band.
        }

        return invitation.ToResponse(family.Name);
    }
}

// ------------------------- List my pending ----------------------------

public sealed record ListMyFamilyInvitationsQuery(
    UserId UserId,
    DateTime UtcNow)
    : IQuery<IReadOnlyList<FamilyInvitationResponse>>;

public sealed class ListMyFamilyInvitationsQueryValidator
    : AbstractValidator<ListMyFamilyInvitationsQuery>
{
    public ListMyFamilyInvitationsQueryValidator()
    {
        RuleFor(q => q.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class ListMyFamilyInvitationsQueryHandler
    : IQueryHandler<
        ListMyFamilyInvitationsQuery,
        IReadOnlyList<FamilyInvitationResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListMyFamilyInvitationsQueryHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<FamilyInvitationResponse>>> Handle(
        ListMyFamilyInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        List<FamilyInvitation> invitations =
            await _dbContext.Invitations
                .AsNoTracking()
                .Where(invitation =>
                    invitation.InvitedUserId == request.UserId &&
                    invitation.Status == FamilyInvitationStatus.Pending &&
                    invitation.ExpiresOnUtc > request.UtcNow)
                .OrderByDescending(invitation =>
                    invitation.CreatedOnUtc)
                .ToListAsync(cancellationToken);

        var familyIds =
            invitations.Select(i => i.FamilyId).Distinct().ToList();

        Dictionary<FamilyId, string> familyNames =
            await _dbContext.Families
                .AsNoTracking()
                .Where(family => familyIds.Contains(family.Id))
                .ToDictionaryAsync(
                    family => family.Id,
                    family => family.Name,
                    cancellationToken);

        IReadOnlyList<FamilyInvitationResponse> items =
            invitations
                .Select(invitation =>
                    invitation.ToResponse(
                        familyNames.TryGetValue(
                            invitation.FamilyId,
                            out string? familyName)
                            ? familyName
                            : null))
                .ToList();

        return Result<IReadOnlyList<FamilyInvitationResponse>>.Success(items);
    }
}

// ------------------------------- Accept -------------------------------

public sealed record AcceptFamilyInvitationCommand(
    UserId InvitedUserId,
    string Token,
    DateTime UtcNow)
    : ICommand;

public sealed class AcceptFamilyInvitationCommandValidator
    : AbstractValidator<AcceptFamilyInvitationCommand>
{
    public AcceptFamilyInvitationCommandValidator()
    {
        RuleFor(c => c.InvitedUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Token).NotEmpty();
    }
}

public sealed class AcceptFamilyInvitationCommandHandler
    : ICommandHandler<AcceptFamilyInvitationCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public AcceptFamilyInvitationCommandHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        AcceptFamilyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        FamilyInvitation? invitation =
            await FindByTokenAsync(request.Token, cancellationToken);

        if (invitation is null)
        {
            return Result.Failure(
                FamilyInvitationErrors.InvitationNotFound);
        }

        Family? family =
            await _dbContext.Families
                .SingleOrDefaultAsync(
                    family => family.Id == invitation.FamilyId,
                    cancellationToken);

        if (family is null)
        {
            return Result.Failure(
                FamilyInvitationErrors.FamilyNotFound);
        }

        try
        {
            invitation.Accept(request.InvitedUserId, request.UtcNow);

            family.AddMember(
                FamilyMember.Create(
                    invitation.InvitedUserId,
                    invitation.CreatedByUserId,
                    invitation.RelationshipType,
                    invitation.Role));
        }
        catch (DomainException exception)
        {
            return Result.Failure(
                MapDomainException(exception));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<FamilyInvitation?> FindByTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string tokenHash = FamilyInvitation.HashToken(token.Trim());

        return await _dbContext.Invitations
            .SingleOrDefaultAsync(
                invitation => invitation.TokenHash == tokenHash,
                cancellationToken);
    }

    private static Error MapDomainException(DomainException exception) =>
        exception.Message switch
        {
            "Only the invited user can respond to this invitation." =>
                FamilyInvitationErrors.NotInvitee,
            "This invitation has expired." =>
                FamilyInvitationErrors.Expired,
            "Member already exists." =>
                FamilyInvitationErrors.AlreadyMember,
            _ => FamilyInvitationErrors.NotPending
        };
}

// ------------------------------- Decline ------------------------------

public sealed record DeclineFamilyInvitationCommand(
    UserId InvitedUserId,
    string Token,
    DateTime UtcNow)
    : ICommand;

public sealed class DeclineFamilyInvitationCommandValidator
    : AbstractValidator<DeclineFamilyInvitationCommand>
{
    public DeclineFamilyInvitationCommandValidator()
    {
        RuleFor(c => c.InvitedUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Token).NotEmpty();
    }
}

public sealed class DeclineFamilyInvitationCommandHandler
    : ICommandHandler<DeclineFamilyInvitationCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public DeclineFamilyInvitationCommandHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        DeclineFamilyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result.Failure(
                FamilyInvitationErrors.InvalidToken);
        }

        string tokenHash =
            FamilyInvitation.HashToken(request.Token.Trim());

        FamilyInvitation? invitation =
            await _dbContext.Invitations
                .SingleOrDefaultAsync(
                    item => item.TokenHash == tokenHash,
                    cancellationToken);

        if (invitation is null)
        {
            return Result.Failure(
                FamilyInvitationErrors.InvitationNotFound);
        }

        try
        {
            invitation.Decline(request.InvitedUserId, request.UtcNow);
        }
        catch (DomainException exception)
        {
            Error mapped = exception.Message switch
            {
                "Only the invited user can respond to this invitation." =>
                    FamilyInvitationErrors.NotInvitee,
                "This invitation has expired." =>
                    FamilyInvitationErrors.Expired,
                _ => FamilyInvitationErrors.NotPending
            };

            return Result.Failure(mapped);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// ------------------------------- Revoke -------------------------------

public sealed record RevokeFamilyInvitationCommand(
    UserId OwnerUserId,
    FamilyInvitationId InvitationId,
    DateTime UtcNow)
    : ICommand;

public sealed class RevokeFamilyInvitationCommandValidator
    : AbstractValidator<RevokeFamilyInvitationCommand>
{
    public RevokeFamilyInvitationCommandValidator()
    {
        RuleFor(c => c.OwnerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.InvitationId)
            .NotEqual(FamilyInvitationId.Empty);
    }
}

public sealed class RevokeFamilyInvitationCommandHandler
    : ICommandHandler<RevokeFamilyInvitationCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public RevokeFamilyInvitationCommandHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RevokeFamilyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        FamilyInvitation? invitation =
            await _dbContext.Invitations
                .SingleOrDefaultAsync(
                    item => item.Id == request.InvitationId,
                    cancellationToken);

        if (invitation is null)
        {
            return Result.Failure(
                FamilyInvitationErrors.InvitationNotFound);
        }

        // Revocation is owner-only and scoped to the family that owns
        // the invitation.
        bool isOwner =
            await _dbContext.Families.AnyAsync(
                family =>
                    family.Id == invitation.FamilyId &&
                    family.OwnerUserId == request.OwnerUserId,
                cancellationToken);

        if (!isOwner)
        {
            return Result.Failure(
                FamilyInvitationErrors.AccessDenied);
        }

        try
        {
            invitation.Revoke(request.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                FamilyInvitationErrors.NotPending);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}