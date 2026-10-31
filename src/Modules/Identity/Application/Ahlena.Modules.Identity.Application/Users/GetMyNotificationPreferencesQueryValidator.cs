using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

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
