using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

/// <summary>
/// Idempotent activation of a FAQ entry.
/// </summary>
public sealed record ActivateHelpFaqCommand(
    HelpFaqId Id)
    : ICommand<HelpFaqResponse>;

public sealed record DeactivateHelpFaqCommand(
    HelpFaqId Id)
    : ICommand<HelpFaqResponse>;

public sealed class ActivateHelpFaqCommandValidator
    : AbstractValidator<ActivateHelpFaqCommand>
{
    public ActivateHelpFaqCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(HelpFaqId.Empty);
    }
}

public sealed class DeactivateHelpFaqCommandValidator
    : AbstractValidator<DeactivateHelpFaqCommand>
{
    public DeactivateHelpFaqCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(HelpFaqId.Empty);
    }
}

public sealed class ActivateHelpFaqCommandHandler :
    ICommandHandler<ActivateHelpFaqCommand, HelpFaqResponse>
{
    private readonly ICmsDbContext _dbContext;

    public ActivateHelpFaqCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<HelpFaqResponse>> Handle(
        ActivateHelpFaqCommand request,
        CancellationToken cancellationToken)
    {
        HelpFaq? faq =
            await _dbContext.HelpFaqs
                .SingleOrDefaultAsync(
                    item => item.Id == request.Id,
                    cancellationToken);

        if (faq is null)
        {
            return HelpErrors.FaqNotFound;
        }

        faq.Activate();

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return faq.ToResponse();
    }
}

public sealed class DeactivateHelpFaqCommandHandler :
    ICommandHandler<DeactivateHelpFaqCommand, HelpFaqResponse>
{
    private readonly ICmsDbContext _dbContext;

    public DeactivateHelpFaqCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<HelpFaqResponse>> Handle(
        DeactivateHelpFaqCommand request,
        CancellationToken cancellationToken)
    {
        HelpFaq? faq =
            await _dbContext.HelpFaqs
                .SingleOrDefaultAsync(
                    item => item.Id == request.Id,
                    cancellationToken);

        if (faq is null)
        {
            return HelpErrors.FaqNotFound;
        }

        faq.Deactivate();

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return faq.ToResponse();
    }
}
