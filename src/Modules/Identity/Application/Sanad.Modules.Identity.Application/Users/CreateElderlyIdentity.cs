using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record CreateElderlyIdentityResponse(
    UserId UserId);

public sealed record CreateElderlyIdentityCommand(
    string ArabicFullName,
    string EnglishFullName,
    string PhoneNumber,
    Gender Gender,
    DateOnly DateOfBirth,
    DateTime UtcNow)
    : ICommand<CreateElderlyIdentityResponse>;

public sealed class CreateElderlyIdentityCommandValidator
    : AbstractValidator<CreateElderlyIdentityCommand>
{
    public CreateElderlyIdentityCommandValidator()
    {
        RuleFor(c => c.ArabicFullName).NotEmpty();
        RuleFor(c => c.EnglishFullName).NotEmpty();
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Gender).IsInEnum();
        RuleFor(c => c.DateOfBirth)
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(c => c.UtcNow.Kind == DateTimeKind.Utc)
            .WithMessage("Date of birth cannot be in the future.");
    }
}

public sealed class CreateElderlyIdentityCommandHandler
    : ICommandHandler<
        CreateElderlyIdentityCommand,
        CreateElderlyIdentityResponse>
{
    private readonly IIdentityDbContext _dbContext;

    public CreateElderlyIdentityCommandHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CreateElderlyIdentityResponse>> Handle(
        CreateElderlyIdentityCommand request,
        CancellationToken cancellationToken)
    {
        FullName arabicName;
        FullName englishName;
        PhoneNumber phone;

        try
        {
            arabicName = FullName.Create(request.ArabicFullName);
            englishName = FullName.Create(request.EnglishFullName);
            phone = PhoneNumber.Create(request.PhoneNumber);
        }
        catch (DomainException)
        {
            return Result<CreateElderlyIdentityResponse>.Failure(
                ElderlyIdentityErrors.InvalidProfile);
        }

        bool takenByNonElderly =
            await _dbContext.Users.AnyAsync(
                u => u.PhoneNumber == phone &&
                     u.Accounts.Any(a => a.AccountType != AccountType.Elderly),
                cancellationToken);

        if (takenByNonElderly)
        {
            return Result<CreateElderlyIdentityResponse>.Failure(
                ElderlyIdentityErrors.PhoneAlreadyInUse);
        }

        User user;
        try
        {
            user = User.CreateElderly(
                arabicName,
                englishName,
                phone,
                request.Gender,
                request.DateOfBirth,
                request.UtcNow);
        }
        catch (DomainException)
        {
            return Result<CreateElderlyIdentityResponse>.Failure(
                ElderlyIdentityErrors.InvalidProfile);
        }

        _dbContext.Users.Add(user);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateElderlyIdentityResponse(user.Id);
    }
}