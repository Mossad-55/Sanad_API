using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed class GetActiveSessionsQueryValidator :
    AbstractValidator<GetActiveSessionsQuery>
{
    public GetActiveSessionsQueryValidator()
    {
        RuleFor(query =>
                query.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}