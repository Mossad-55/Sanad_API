using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class GetMyUiLanguageQueryValidator :
    AbstractValidator<GetMyUiLanguageQuery>
{
    public GetMyUiLanguageQueryValidator()
    {
        RuleFor(query =>
                query.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}
