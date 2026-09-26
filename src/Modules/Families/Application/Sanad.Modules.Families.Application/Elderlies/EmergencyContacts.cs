using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Elderlies;

public sealed record EmergencyContactResponse(string Name, string Relationship, string PhoneNumber);
public sealed record GetFamilyEmergencyContactQuery(UserId UserId, ElderlyId DependentId) : IQuery<EmergencyContactResponse?>;
public sealed record SetFamilyEmergencyContactCommand(UserId UserId, ElderlyId DependentId, string Name, string Relationship, string PhoneNumber) : ICommand<EmergencyContactResponse>;

public sealed class SetFamilyEmergencyContactCommandValidator : AbstractValidator<SetFamilyEmergencyContactCommand>
{
    public SetFamilyEmergencyContactCommandValidator()
    {
        RuleFor(x => x.UserId).NotEqual(UserId.Empty);
        RuleFor(x => x.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Elderly.MaximumEmergencyContactNameLength);
        RuleFor(x => x.Relationship).NotEmpty().MaximumLength(Elderly.MaximumEmergencyContactRelationshipLength);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^\+[1-9][0-9]{1,14}$");
    }
}

public sealed class GetFamilyEmergencyContactQueryHandler(IFamiliesDbContext db)
    : IQueryHandler<GetFamilyEmergencyContactQuery, EmergencyContactResponse?>
{
    public async Task<Result<EmergencyContactResponse?>> Handle(GetFamilyEmergencyContactQuery request, CancellationToken cancellationToken)
    {
        var familyId = await db.Families.AsNoTracking()
            .Where(f => f.DeletedOnUtc == null
                && (f.OwnerUserId == request.UserId || f.Members.Any(m => m.Id == request.UserId)))
            .Select(f => (FamilyId?)f.Id).SingleOrDefaultAsync(cancellationToken);
        if (familyId is null) return ElderlyErrors.FamilyNotFound;
        Elderly? elderly = await db.Elderlies.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == request.DependentId && e.FamilyId == familyId.Value, cancellationToken);
        if (elderly is null) return ElderlyErrors.NotFound;
        return Map(elderly);
    }

    internal static EmergencyContactResponse? Map(Elderly elderly) => elderly.EmergencyContactName is null ? null :
        new(elderly.EmergencyContactName, elderly.EmergencyContactRelationship!, elderly.EmergencyContactPhoneNumber!);
}

public sealed class SetFamilyEmergencyContactCommandHandler(IFamiliesDbContext db)
    : ICommandHandler<SetFamilyEmergencyContactCommand, EmergencyContactResponse>
{
    public async Task<Result<EmergencyContactResponse>> Handle(SetFamilyEmergencyContactCommand request, CancellationToken cancellationToken)
    {
        Family? family = await db.Families.SingleOrDefaultAsync(
            f => f.OwnerUserId == request.UserId && f.DeletedOnUtc == null,
            cancellationToken);
        if (family is null) return ElderlyErrors.AccessDenied;
        Elderly? elderly = await db.Elderlies.SingleOrDefaultAsync(e => e.Id == request.DependentId && e.FamilyId == family.Id, cancellationToken);
        if (elderly is null) return ElderlyErrors.NotFound;
        try { elderly.SetEmergencyContact(request.Name, request.Relationship, request.PhoneNumber); }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException) { return ElderlyErrors.InvalidProfile; }
        await db.SaveChangesAsync(cancellationToken);
        return GetFamilyEmergencyContactQueryHandler.Map(elderly)!;
    }
}
