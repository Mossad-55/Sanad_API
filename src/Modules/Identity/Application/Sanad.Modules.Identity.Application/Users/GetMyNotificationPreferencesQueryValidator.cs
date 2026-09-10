using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class GetMyNotificationPreferencesQueryValidator :
    AbstractValidator<GetMyNotificationPreferencesQuery>
{
    public GetMyNotificationPreferencesQueryValidator()
    {
        RuleFor(query =>
                query.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}
