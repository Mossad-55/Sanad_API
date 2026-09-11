using FluentValidation;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

internal static class FamilyMemberEnrichment
{
    public static async Task<IReadOnlyDictionary<UserId, FamilyMemberProfile>>
        ResolveProfilesAsync(
            IFamilyIdentityGateway gateway,
            Family family,
            CancellationToken cancellationToken)
    {
        UserId[] userIds =
            family.Members
                .Select(member => member.Id)
                .Distinct()
                .ToArray();

        IReadOnlyList<FamilyMemberProfile> profiles =
            await gateway.GetFamilyMemberProfilesAsync(
                userIds,
                cancellationToken);

        return profiles.ToDictionary(
            profile => profile.UserId);
    }

    public static async Task<FamilyMemberProfile?> ResolveProfileAsync(
        IFamilyIdentityGateway gateway,
        UserId userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FamilyMemberProfile> profiles =
            await gateway.GetFamilyMemberProfilesAsync(
                [userId],
                cancellationToken);

        return profiles.FirstOrDefault(
            profile => profile.UserId == userId);
    }
}

// ------------------------------ Get member -----------------------------

public sealed record GetFamilyMemberQuery(
    UserId CallerUserId,
    UserId MemberUserId)
    : IQuery<FamilyMemberResponse>;

public sealed class GetFamilyMemberQueryValidator
    : AbstractValidator<GetFamilyMemberQuery>
{
    public GetFamilyMemberQueryValidator()
    {
        RuleFor(q => q.CallerUserId).NotEqual(UserId.Empty);
        RuleFor(q => q.MemberUserId).NotEqual(UserId.Empty);
    }
}

public sealed class GetFamilyMemberQueryHandler
    : IQueryHandler<GetFamilyMemberQuery, FamilyMemberResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IFamilyIdentityGateway _identityGateway;

    public GetFamilyMemberQueryHandler(
        IFamiliesDbContext dbContext,
        IFamilyIdentityGateway identityGateway)
    {
        _dbContext = dbContext;
        _identityGateway = identityGateway;
    }

    public async Task<Result<FamilyMemberResponse>> Handle(
        GetFamilyMemberQuery request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.CallerUserId,
                cancellationToken);

        if (family is null)
        {
            return FamilyErrors.NotFound;
        }

        if (!FamilyAccess.IsMember(family, request.CallerUserId))
        {
            return FamilyErrors.AccessDenied;
        }

        FamilyMember? member =
            family.Members.FirstOrDefault(
                m => m.Id == request.MemberUserId);

        if (member is null)
        {
            return FamilyErrors.MemberNotFound;
        }

        FamilyMemberProfile? profile =
            await FamilyMemberEnrichment.ResolveProfileAsync(
                _identityGateway,
                member.Id,
                cancellationToken);

        var profiles = new Dictionary<UserId, FamilyMemberProfile>();

        if (profile is not null)
        {
            profiles[profile.UserId] = profile;
        }

        return member.ToResponse(profiles);
    }
}

// ---------------------------- Change role ------------------------------

public sealed record ChangeFamilyMemberRoleCommand(
    UserId CallerUserId,
    UserId MemberUserId,
    FamilyRole Role)
    : ICommand;

public sealed class ChangeFamilyMemberRoleCommandValidator
    : AbstractValidator<ChangeFamilyMemberRoleCommand>
{
    public ChangeFamilyMemberRoleCommandValidator()
    {
        RuleFor(c => c.CallerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.MemberUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Role)
            .IsInEnum()
            .Must(role => role != FamilyRole.Owner)
            .WithMessage("Invited members can only be Editors or Viewers.");
    }
}

public sealed class ChangeFamilyMemberRoleCommandHandler
    : ICommandHandler<ChangeFamilyMemberRoleCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public ChangeFamilyMemberRoleCommandHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        ChangeFamilyMemberRoleCommand request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.CallerUserId,
                cancellationToken);

        if (family is null)
        {
            return Result.Failure(
                FamilyErrors.NotFound);
        }

        if (!FamilyAccess.IsOwner(family, request.CallerUserId))
        {
            return Result.Failure(
                FamilyErrors.NotOwner);
        }

        FamilyRole? currentRole =
            family.GetRole(request.MemberUserId);

        if (currentRole is null)
        {
            return Result.Failure(
                FamilyErrors.MemberNotFound);
        }

        if (currentRole == FamilyRole.Owner)
        {
            return Result.Failure(
                FamilyErrors.OwnerProtected);
        }

        try
        {
            family.ChangeMemberRole(
                request.MemberUserId,
                request.Role);
        }
        catch (DomainException exception)
        {
            return Result.Failure(exception.Message ==
                "The family member was not found."
                ? FamilyErrors.MemberNotFound
                : FamilyErrors.OwnerProtected);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// ------------------------------ Remove --------------------------------

public sealed record RemoveFamilyMemberCommand(
    UserId CallerUserId,
    UserId MemberUserId)
    : ICommand;

public sealed class RemoveFamilyMemberCommandValidator
    : AbstractValidator<RemoveFamilyMemberCommand>
{
    public RemoveFamilyMemberCommandValidator()
    {
        RuleFor(c => c.CallerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.MemberUserId).NotEqual(UserId.Empty);
    }
}

public sealed class RemoveFamilyMemberCommandHandler
    : ICommandHandler<RemoveFamilyMemberCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public RemoveFamilyMemberCommandHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RemoveFamilyMemberCommand request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.CallerUserId,
                cancellationToken);

        if (family is null)
        {
            return Result.Failure(
                FamilyErrors.NotFound);
        }

        if (!FamilyAccess.IsOwner(family, request.CallerUserId))
        {
            return Result.Failure(
                FamilyErrors.NotOwner);
        }

        FamilyRole? currentRole =
            family.GetRole(request.MemberUserId);

        if (currentRole is null)
        {
            return Result.Failure(
                FamilyErrors.MemberNotFound);
        }

        if (currentRole == FamilyRole.Owner)
        {
            return Result.Failure(
                FamilyErrors.OwnerProtected);
        }

        family.RemoveMember(request.MemberUserId);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
