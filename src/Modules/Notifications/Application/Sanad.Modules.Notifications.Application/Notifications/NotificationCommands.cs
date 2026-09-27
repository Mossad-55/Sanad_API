using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
    string Category, string Type, string Title, string Body, DateTime CreatedOnUtc,
    DateOnly LocalDate = default)
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
        var userIds = (await recipients.GetCheckInAlertRecipientsAsync(r.Elderly, ct)).Distinct().ToArray();
        var keys = r.LocalDate == default ? [] : userIds.Select(x => $"elderly-check-in:{r.Elderly.ElderlyEntityId:N}:{r.LocalDate:yyyy-MM-dd}:{x.Value:N}").ToArray();
        var existing = keys.Length == 0 ? [] : await db.Notifications.Where(x => x.IdempotencyKey != null && keys.Contains(x.IdempotencyKey!)).Select(x => x.IdempotencyKey!).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            var key = r.LocalDate == default ? null : $"elderly-check-in:{r.Elderly.ElderlyEntityId:N}:{r.LocalDate:yyyy-MM-dd}:{userId.Value:N}";
            if (key is not null && existing.Contains(key, StringComparer.Ordinal)) continue;
            db.Notifications.Add(Notification.Create(userId.Value, r.Category.Trim(), r.Type.Trim(), r.Title.Trim(), r.Body.Trim(), "Elderly", r.Elderly.ElderlyEntityId, r.CreatedOnUtc, key));
        }
        await db.SaveChangesAsync(ct);
        return userIds.Length;
    }
}

public sealed record CreateHelpRequestAlertNotificationsCommand(
    Abstractions.Recipients.ElderlyRecipient Elderly, Guid HelpRequestId,
    DateTime CreatedOnUtc) : ICommand<int>;
public sealed class CreateHelpRequestAlertNotificationsCommandHandler(
    INotificationsDbContext db, Abstractions.Recipients.INotificationRecipientGateway recipients)
    : ICommandHandler<CreateHelpRequestAlertNotificationsCommand, int>
{
    public async Task<Result<int>> Handle(CreateHelpRequestAlertNotificationsCommand r, CancellationToken ct)
    {
        var users = (await recipients.GetHelpRequestAlertRecipientsAsync(r.Elderly, ct))
            .Distinct()
            .ToArray();
        var keys = users
            .Select(user => $"elderly-help-request:{r.HelpRequestId:N}:{user.Value:N}")
            .ToArray();
        var existing = (await db.Notifications
                .Where(notification => notification.IdempotencyKey != null && keys.Contains(notification.IdempotencyKey!))
                .Select(notification => notification.IdempotencyKey!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var created = 0;
        var pending = new List<Notification>();
        foreach (var user in users)
        {
            var key = $"elderly-help-request:{r.HelpRequestId:N}:{user.Value:N}";
            if (!existing.Add(key))
                continue;

            var notification = Notification.Create(
                user.Value,
                "ElderlyHelpRequest",
                "HelpRequestCreated",
                "Help request",
                "An elderly family member created a help request.",
                "HelpRequest",
                r.HelpRequestId,
                r.CreatedOnUtc,
                key);
            db.Notifications.Add(notification);
            pending.Add(notification);
            created++;
        }

        if (created > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent producer may have won the unique idempotency-key race.
                // Only treat the failure as an idempotent replay when every key now exists;
                // unrelated persistence failures must still surface to the caller.
                if (db is DbContext context)
                {
                    foreach (var notification in pending)
                        context.Entry(notification).State = EntityState.Detached;
                }

                var committedKeys = await db.Notifications
                    .Where(notification => notification.IdempotencyKey != null && keys.Contains(notification.IdempotencyKey!))
                    .Select(notification => notification.IdempotencyKey!)
                    .ToListAsync(ct);
                if (committedKeys.ToHashSet(StringComparer.Ordinal).Count != keys.Length)
                    throw;

                return 0;
            }
        }

        return created;
    }
}
