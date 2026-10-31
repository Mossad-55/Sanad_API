using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

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
