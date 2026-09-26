using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Elderlies;

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
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
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
