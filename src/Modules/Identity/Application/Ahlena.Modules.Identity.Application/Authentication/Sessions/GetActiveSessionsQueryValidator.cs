using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Sessions;

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