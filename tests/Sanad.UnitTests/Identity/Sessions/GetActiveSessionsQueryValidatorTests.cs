using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Sessions;

namespace Sanad.UnitTests.Identity.Sessions;

public sealed class GetActiveSessionsQueryValidatorTests
{
    private readonly GetActiveSessionsQueryValidator
        _validator = new();

    [Fact]
    public void Validate_ShouldAcceptValidQuery()
    {
        GetActiveSessionsQuery query =
            new(
                UserId.New());

        _validator
            .TestValidate(query)
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldRejectEmptyUserId()
    {
        GetActiveSessionsQuery query =
            new(
                UserId.Empty);

        _validator
            .TestValidate(query)
            .ShouldHaveValidationErrorFor(
                value =>
                    value.CurrentUserId);
    }
}