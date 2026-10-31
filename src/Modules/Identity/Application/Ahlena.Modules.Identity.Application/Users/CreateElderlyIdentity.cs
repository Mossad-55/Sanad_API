using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

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