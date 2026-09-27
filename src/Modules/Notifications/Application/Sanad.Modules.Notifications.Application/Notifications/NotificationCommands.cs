using FluentValidation;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Notifications.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Domain.Notifications;

namespace Sanad.Modules.Notifications.Application.Notifications;

public sealed record CreateInAppNotificationCommand(
    Guid RecipientUserId, string Category, string Type, string Title, string Body,
    string DestinationEntityKind, Guid DestinationEntityId, DateTime CreatedOnUtc) : ICommand<Guid>;

public sealed class CreateInAppNotificationCommandValidator : AbstractValidator<CreateInAppNotificationCommand>
{
    public CreateInAppNotificationCommandValidator()
    {
        RuleFor(x => x.RecipientUserId).NotEmpty(); RuleFor(x => x.DestinationEntityId).NotEmpty();
        RuleFor(x => x.Category).NotEmpty().MaximumLength(Notification.MaximumCategoryLength);
        RuleFor(x => x.Type).NotEmpty().MaximumLength(Notification.MaximumTypeLength);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Notification.MaximumTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Notification.MaximumBodyLength);
        RuleFor(x => x.DestinationEntityKind).NotEmpty().MaximumLength(Notification.MaximumDestinationKindLength);
        RuleFor(x => x.CreatedOnUtc).Must(x => x.Kind == DateTimeKind.Utc);
    }
}

public sealed class CreateInAppNotificationCommandHandler(INotificationsDbContext db)
    : ICommandHandler<CreateInAppNotificationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateInAppNotificationCommand r, CancellationToken ct)
    {
        var notification = Notification.Create(r.RecipientUserId, r.Category.Trim(), r.Type.Trim(),
            r.Title.Trim(), r.Body.Trim(), r.DestinationEntityKind.Trim(), r.DestinationEntityId, r.CreatedOnUtc);
        db.Notifications.Add(notification); await db.SaveChangesAsync(ct); return notification.Id;
    }
}

/// <summary>
/// Durable check-in alert creation seam. Recipient eligibility is resolved by
/// the Families gateway, while Identity remains the owner of preferences.
/// All eligible inbox rows are committed together; delivery providers are not
/// part of this boundary.
/// </summary>
public sealed record CreateCheckInAlertNotificationsCommand(
    Abstractions.Recipients.ElderlyRecipient Elderly,
    string Category, string Type, string Title, string Body, DateTime CreatedOnUtc)
    : ICommand<int>;

public sealed class CreateCheckInAlertNotificationsCommandValidator : AbstractValidator<CreateCheckInAlertNotificationsCommand>
{
    public CreateCheckInAlertNotificationsCommandValidator()
    {
        RuleFor(x => x.Elderly.ElderlyIdentityUserId).NotEqual(Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId.Empty);
        RuleFor(x => x.Elderly.ElderlyEntityId).NotEmpty(); RuleFor(x => x.Category).NotEmpty().MaximumLength(Notification.MaximumCategoryLength);
        RuleFor(x => x.Type).NotEmpty().MaximumLength(Notification.MaximumTypeLength); RuleFor(x => x.Title).NotEmpty().MaximumLength(Notification.MaximumTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Notification.MaximumBodyLength); RuleFor(x => x.CreatedOnUtc).Must(x => x.Kind == DateTimeKind.Utc);
    }
}

public sealed class CreateCheckInAlertNotificationsCommandHandler(
    INotificationsDbContext db,
    Abstractions.Recipients.INotificationRecipientGateway recipients)
    : ICommandHandler<CreateCheckInAlertNotificationsCommand, int>
{
    public async Task<Result<int>> Handle(CreateCheckInAlertNotificationsCommand r, CancellationToken ct)
    {
        var userIds = await recipients.GetCheckInAlertRecipientsAsync(r.Elderly, ct);
        foreach (var userId in userIds.Distinct())
            db.Notifications.Add(Notification.Create(userId.Value, r.Category.Trim(), r.Type.Trim(), r.Title.Trim(), r.Body.Trim(), "Elderly", r.Elderly.ElderlyEntityId, r.CreatedOnUtc));
        await db.SaveChangesAsync(ct);
        return userIds.Distinct().Count();
    }
}
