using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Application.Elderlies;

public sealed record ChangeDependentTimeZoneCommand(
    UserId UserId,
    ElderlyId DependentId,
    string TimeZoneId)
    : ICommand<DependentTimeZoneResponse>;

public sealed record DependentTimeZoneResponse(
    ElderlyId DependentId,
    string TimeZoneId,
    DateTime UpdatedOnUtc);

public sealed class ChangeDependentTimeZoneCommandValidator
    : AbstractValidator<ChangeDependentTimeZoneCommand>
{
    public ChangeDependentTimeZoneCommandValidator()
    {
        RuleFor(x => x.UserId).NotEqual(UserId.Empty);
        RuleFor(x => x.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(ElderlyTimeZone.MaximumIdLength);
    }
}

public sealed class ChangeDependentTimeZoneCommandHandler(IFamiliesDbContext dbContext)
    : ICommandHandler<ChangeDependentTimeZoneCommand, DependentTimeZoneResponse>
{
    public async Task<Result<DependentTimeZoneResponse>> Handle(
        ChangeDependentTimeZoneCommand request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(
            dbContext,
            request.UserId,
            cancellationToken);
        if (family is null)
            return ElderlyErrors.FamilyNotFound;
        if (!FamilyAccess.IsOwner(family, request.UserId))
            return ElderlyErrors.AccessDenied;

        Elderly? dependent = await dbContext.Elderlies.SingleOrDefaultAsync(
            x => x.Id == request.DependentId && x.FamilyId == family.Id,
            cancellationToken);
        if (dependent is null)
            return ElderlyErrors.NotFound;

        try
        {
            dependent.ChangeTimeZone(request.TimeZoneId);
        }
        catch (Ahlena.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return ElderlyErrors.InvalidProfile;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new DependentTimeZoneResponse(
            dependent.Id,
            dependent.TimeZoneId,
            dependent.UpdatedOnUtc);
    }
}
