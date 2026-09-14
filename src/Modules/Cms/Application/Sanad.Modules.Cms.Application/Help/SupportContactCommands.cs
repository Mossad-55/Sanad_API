using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

/// <summary>
/// Reads the one global support contact row.
/// </summary>
public sealed record GetSupportContactQuery
    : IQuery<SupportContactResponse>;

public sealed class GetSupportContactQueryHandler
    : IQueryHandler<GetSupportContactQuery, SupportContactResponse>
{
    private readonly ICmsDbContext _dbContext;

    public GetSupportContactQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SupportContactResponse>> Handle(
        GetSupportContactQuery request,
        CancellationToken cancellationToken)
    {
        SupportContact? contact =
            await _dbContext.SupportContacts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == SupportContact.SingletonId,
                    cancellationToken);

        if (contact is null)
        {
            return HelpErrors.SupportContactNotFound;
        }

        return contact.ToResponse();
    }
}

/// <summary>
/// Upserts the single global support contact row. Contact-card data only:
/// nothing here sends an SMS or an email.
/// </summary>
public sealed record UpsertSupportContactCommand(
    string SupportPhone,
    string SupportEmail)
    : ICommand<SupportContactResponse>;

public sealed class UpsertSupportContactCommandValidator
    : AbstractValidator<UpsertSupportContactCommand>
{
    public UpsertSupportContactCommandValidator()
    {
        RuleFor(command => command.SupportPhone)
            .NotEmpty()
            .Matches(@"\A\+[1-9][0-9]{1,14}\z")
            .WithMessage(
                "Support phone must be in E.164 format.")
            .MaximumLength(
                SupportContact.MaximumPhoneLength);

        RuleFor(command => command.SupportEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(
                SupportContact.MaximumEmailLength);
    }
}

public sealed class UpsertSupportContactCommandHandler
    : ICommandHandler<
        UpsertSupportContactCommand,
        SupportContactResponse>
{
    private readonly ICmsDbContext _dbContext;

    public UpsertSupportContactCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SupportContactResponse>> Handle(
        UpsertSupportContactCommand request,
        CancellationToken cancellationToken)
    {
        SupportContact? contact =
            await _dbContext.SupportContacts
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == SupportContact.SingletonId,
                    cancellationToken);

        if (contact is null)
        {
            contact = SupportContact.Create(
                request.SupportPhone,
                request.SupportEmail);

            _dbContext.SupportContacts.Add(contact);
        }
        else
        {
            contact.Update(
                request.SupportPhone,
                request.SupportEmail);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return contact.ToResponse();
    }
}
