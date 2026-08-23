using FluentValidation;
using Sanad.BuildingBlocks.Application.Behaviors;

namespace Sanad.UnitTests.BuildingBlocks;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_ShouldExecuteNext_WhenNoValidatorsExist()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        bool nextCalled = false;

        string result = await behavior.Handle(
            new TestRequest("value"),
            cancellationToken =>
            {
                nextCalled = true;
                return Task.FromResult("success");
            },
            CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal("success", result);
    }

    [Fact]
    public async Task Handle_ShouldExecuteNext_WhenValidationSucceeds()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
        [
            new TestRequestValidator()
        ]);

        bool nextCalled = false;

        string result = await behavior.Handle(
            new TestRequest("valid-value"),
            cancellationToken =>
            {
                nextCalled = true;
                return Task.FromResult("success");
            },
            CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal("success", result);
    }

    [Fact]
    public async Task Handle_ShouldThrowValidationException_WhenValidationFails()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
        [
            new TestRequestValidator()
        ]);

        bool nextCalled = false;

        ValidationException exception =
            await Assert.ThrowsAsync<ValidationException>(
                () => behavior.Handle(
                    new TestRequest(string.Empty),
                    cancellationToken =>
                    {
                        nextCalled = true;
                        return Task.FromResult("success");
                    },
                    CancellationToken.None));

        Assert.False(nextCalled);
        Assert.Contains(
            exception.Errors,
            failure => failure.PropertyName == nameof(TestRequest.Value));
    }

    [Fact]
    public async Task Handle_ShouldIncludeFailures_FromAllValidators()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
        [
            new ExpectedValueValidator(),
            new MinimumLengthValidator()
        ]);

        ValidationException exception =
            await Assert.ThrowsAsync<ValidationException>(
                () => behavior.Handle(
                    new TestRequest("x"),
                    cancellationToken => Task.FromResult("success"),
                    CancellationToken.None));

        Assert.True(exception.Errors.Count() >= 2);

        Assert.Contains(
            exception.Errors,
            failure => failure.PropertyName == nameof(TestRequest.Value));

        Assert.Contains(
            exception.Errors,
            failure => failure.ErrorMessage.Contains(
                "expected",
                StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            exception.Errors,
            failure => failure.ErrorMessage.Contains(
                "at least",
                StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TestRequest(string Value);

    private sealed class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(request => request.Value).NotEmpty();
        }
    }

    private sealed class ExpectedValueValidator : AbstractValidator<TestRequest>
    {
        public ExpectedValueValidator()
        {
            RuleFor(request => request.Value).Equal("expected");
        }
    }

    private sealed class MinimumLengthValidator : AbstractValidator<TestRequest>
    {
        public MinimumLengthValidator()
        {
            RuleFor(request => request.Value).MinimumLength(2);
        }
    }
}
