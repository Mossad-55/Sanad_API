using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class GetMyProfileQueryValidator :
    AbstractValidator<GetMyProfileQuery>
{
    public GetMyProfileQueryValidator()
    {
        RuleFor(query =>
                query.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}
