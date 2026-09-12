using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

public sealed record FamilyMemberResponse(
    UserId UserId,
    FamilyRole Role,
    FamilyRelationshipType RelationshipType,
    UserId AddedByUserId,
    DateTime JoinedOnUtc,
    string ArabicFullName,
    string EnglishFullName,
    string? Email);

public sealed record FamilyResponse(
    FamilyId Id,
    string Name,
    UserId OwnerUserId,
    DateTime CreatedOnUtc,
    IReadOnlyList<FamilyMemberResponse> Members);

internal static class FamilyMappings
{
    public static FamilyResponse ToResponse(this Family family) =>
        family.ToResponse(
            new Dictionary<UserId, FamilyMemberProfile>());

    public static FamilyResponse ToResponse(
        this Family family,
        IReadOnlyDictionary<UserId, FamilyMemberProfile> profiles) =>
        new(
            family.Id,
            family.Name,
            family.OwnerUserId,
            family.CreatedOnUtc,
            family.Members
                .Select(m => m.ToResponse(profiles))
                .ToList());

    public static FamilyMemberResponse ToResponse(
        this FamilyMember member,
        IReadOnlyDictionary<UserId, FamilyMemberProfile> profiles)
    {
        profiles.TryGetValue(member.Id, out FamilyMemberProfile? profile);

        return new FamilyMemberResponse(
            member.Id,
            member.Role,
            member.RelationshipType,
            member.AddedByUserId,
            member.JoinedOnUtc,
            profile?.ArabicFullName ?? string.Empty,
            profile?.EnglishFullName ?? string.Empty,
            profile?.Email);
    }
}

// ------------------------------ Bootstrap -----------------------------

public sealed record BootstrapFamilyCommand(
    UserId OwnerUserId,
    string? FamilyName)
    : ICommand<FamilyResponse>;

public sealed class BootstrapFamilyCommandValidator
    : AbstractValidator<BootstrapFamilyCommand>
{
    public BootstrapFamilyCommandValidator()
    {
        RuleFor(c => c.OwnerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.FamilyName)
            .MaximumLength(Family.MaximumNameLength)
            .When(c => c.FamilyName is not null);
    }
}

public sealed class BootstrapFamilyCommandHandler
    : ICommandHandler<BootstrapFamilyCommand, FamilyResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public BootstrapFamilyCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<FamilyResponse>> Handle(
        BootstrapFamilyCommand request,
        CancellationToken cancellationToken)
    {
        bool exists =
            await _dbContext.Families.AnyAsync(
                f => f.OwnerUserId == request.OwnerUserId && f.DeletedOnUtc == null,
                cancellationToken);

        if (exists)
        {
            return FamilyErrors.AlreadyExists;
        }

        Family family =
            Family.Create(
                request.OwnerUserId,
                request.FamilyName);

        _dbContext.Families.Add(family);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return family.ToResponse();
    }
}

// ------------------------------- Get own ------------------------------

public sealed record GetMyFamilyQuery(
    UserId UserId)
    : IQuery<FamilyResponse>;

public sealed class GetMyFamilyQueryHandler
    : IQueryHandler<GetMyFamilyQuery, FamilyResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IFamilyIdentityGateway _identityGateway;

    public GetMyFamilyQueryHandler(
        IFamiliesDbContext dbContext,
        IFamilyIdentityGateway identityGateway)
    {
        _dbContext = dbContext;
        _identityGateway = identityGateway;
    }

    public async Task<Result<FamilyResponse>> Handle(
        GetMyFamilyQuery request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.UserId,
                cancellationToken);

        if (family is null)
        {
            return FamilyErrors.NotFound;
        }

        IReadOnlyDictionary<UserId, FamilyMemberProfile> profiles =
            await FamilyMemberEnrichment.ResolveProfilesAsync(
                _identityGateway,
                family,
                cancellationToken);

        return family.ToResponse(profiles);
    }
}

// ------------------------------- Rename -------------------------------

public sealed record RenameFamilyCommand(
    UserId UserId,
    string Name)
    : ICommand<FamilyResponse>;

public sealed class RenameFamilyCommandValidator
    : AbstractValidator<RenameFamilyCommand>
{
    public RenameFamilyCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(Family.MaximumNameLength);
    }
}

public sealed class RenameFamilyCommandHandler
    : ICommandHandler<RenameFamilyCommand, FamilyResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public RenameFamilyCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<FamilyResponse>> Handle(
        RenameFamilyCommand request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await _dbContext.Families
                .SingleOrDefaultAsync(
                    f => f.OwnerUserId == request.UserId,
                    cancellationToken);

        if (family is null)
        {
            return FamilyErrors.NotFound;
        }

        if (!FamilyAccess.IsOwner(family, request.UserId))
        {
            return FamilyErrors.NotOwner;
        }

        try
        {
            family.Rename(request.Name);
        }
        catch (DomainException)
        {
            return FamilyErrors.InvalidName;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return family.ToResponse();
    }
}