using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Help;

namespace Ahlena.Modules.Cms.Application.Help;

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
            // No row is a valid unconfigured state, not a missing-resource
            // error: the admin read answers 200 with both fields null.
            return new SupportContactResponse(
                null,
                null,
                default,
                default);
        }

        return contact.ToResponse();
    }
}

/// <summary>
/// Upserts the single global support contact row. Contact-card data only:
/// nothing here sends an SMS or an email. Each value is independent —
/// a null value clears that field and leaves the other untouched; a blank
/// string is rejected by validation, never treated as a clear.
/// </summary>
public sealed record UpsertSupportContactCommand(
    string? SupportPhone,
    string? SupportEmail)
    : ICommand<SupportContactResponse>;

public sealed class UpsertSupportContactCommandValidator
    : AbstractValidator<UpsertSupportContactCommand>
{
    public UpsertSupportContactCommandValidator()
    {
        // Null means "leave this field cleared", so the format rules apply
        // only to values the caller actually supplies.
        RuleFor(command => command.SupportPhone)
            .Matches(@"\A\+[1-9][0-9]{1,14}\z")
            .WithMessage(
                "Support phone must be in E.164 format.")
            .When(
                command =>
                    command.SupportPhone is not null)
            .MaximumLength(
                SupportContact.MaximumPhoneLength);

        RuleFor(command => command.SupportEmail)
            .EmailAddress()
            .When(
                command =>
                    command.SupportEmail is not null)
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
