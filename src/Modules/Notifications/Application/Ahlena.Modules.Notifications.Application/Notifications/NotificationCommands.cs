using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Notifications.Application.Abstractions.Data;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;
using Ahlena.Modules.Notifications.Domain.Notifications;

namespace Ahlena.Modules.Notifications.Application.Notifications;

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
        RuleFor(x => x.Elderly.ElderlyIdentityUserId).NotEqual(Ahlena.BuildingBlocks.Domain.Primitives.Ids.UserId.Empty);
        RuleFor(x => x.Elderly.ElderlyEntityId).NotEmpty(); RuleFor(x => x.Category).NotEmpty().MaximumLength(Notification.MaximumCategoryLength);
        RuleFor(x => x.Type).NotEmpty().MaximumLength(Notification.MaximumTypeLength); RuleFor(x => x.Title).NotEmpty().MaximumLength(Notification.MaximumTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Notification.MaximumBodyLength); RuleFor(x => x.CreatedOnUtc).Must(x => x.Kind == DateTimeKind.Utc);
    }
}

public sealed class CreateCheckInAlertNotificationsCommandHandler(
    INotificationsDbContext db,
    INotificationRecipientGateway recipients,
    INotificationLanguageGateway languages)
    : ICommandHandler<CreateCheckInAlertNotificationsCommand, int>
{
    public async Task<Result<int>> Handle(CreateCheckInAlertNotificationsCommand r, CancellationToken ct)
    {
        var userIds = (await recipients.GetCheckInAlertRecipientsAsync(r.Elderly, ct)).Distinct().ToArray();
        var keys = r.LocalDate == default ? [] : userIds.Select(x => $"elderly-check-in:{r.Elderly.ElderlyEntityId:N}:{r.LocalDate:yyyy-MM-dd}:{x.Value:N}").ToArray();
        var existing = keys.Length == 0 ? [] : await db.Notifications.Where(x => x.IdempotencyKey != null && keys.Contains(x.IdempotencyKey!)).Select(x => x.IdempotencyKey!).ToListAsync(ct);
        var recipientLanguages = await languages.GetUiLanguagesAsync(userIds.Select(x => x.Value).ToArray(), ct);
        foreach (var userId in userIds)
        {
            var key = r.LocalDate == default ? null : $"elderly-check-in:{r.Elderly.ElderlyEntityId:N}:{r.LocalDate:yyyy-MM-dd}:{userId.Value:N}";
            if (key is not null && existing.Contains(key, StringComparer.Ordinal)) continue;
            // F8: one batch language lookup covers the whole fan-out. The catalog
            // owns the negative-check-in copy; the copy the command carries stays
            // the fallback for any pair the catalog does not own.
            var copy = NotificationTextCatalog.TryResolveInboundCopy(r.Category.Trim(), r.Type.Trim(),
                NotificationTextCatalog.ResolveLanguage(recipientLanguages, userId.Value), out var resolved)
                ? resolved
                : new NotificationCopy(r.Title.Trim(), r.Body.Trim());
            db.Notifications.Add(Notification.Create(userId.Value, r.Category.Trim(), r.Type.Trim(), copy.Title, copy.Body, "Elderly", r.Elderly.ElderlyEntityId, r.CreatedOnUtc, key));
        }
        await db.SaveChangesAsync(ct);
        return userIds.Length;
    }
}

public sealed record CreateHelpRequestAlertNotificationsCommand(
    Abstractions.Recipients.ElderlyRecipient Elderly, Guid HelpRequestId,
    DateTime CreatedOnUtc) : ICommand<int>;
public sealed class CreateHelpRequestAlertNotificationsCommandHandler(
    INotificationsDbContext db, INotificationRecipientGateway recipients, INotificationLanguageGateway languages)
    : ICommandHandler<CreateHelpRequestAlertNotificationsCommand, int>
{
    public async Task<Result<int>> Handle(CreateHelpRequestAlertNotificationsCommand r, CancellationToken ct)
    {
        var users = (await recipients.GetHelpRequestAlertRecipientsAsync(r.Elderly, ct))
            .Distinct()
            .ToArray();
        var recipientLanguages = await languages.GetUiLanguagesAsync(users.Select(user => user.Value).ToArray(), ct);
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

            // F8: the copy is chosen per recipient from the batch language lookup.
            var copy = NotificationTextCatalog.HelpRequest(
                NotificationTextCatalog.ResolveLanguage(recipientLanguages, user.Value));
            var notification = Notification.Create(
                user.Value,
                "ElderlyHelpRequest",
                "HelpRequestCreated",
                copy.Title,
                copy.Body,
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

public sealed record CreateMedicationLateAlertNotificationsCommand(
    Abstractions.Recipients.ElderlyRecipient Elderly,
    Guid MedicationId, DateOnly ScheduledDate, TimeOnly ScheduledTime, DateTime CreatedOnUtc) : ICommand<int>;

public sealed class CreateMedicationLateAlertNotificationsCommandHandler(
    INotificationsDbContext db, INotificationRecipientGateway recipients, INotificationLanguageGateway languages)
    : ICommandHandler<CreateMedicationLateAlertNotificationsCommand, int>
{
    public async Task<Result<int>> Handle(CreateMedicationLateAlertNotificationsCommand r, CancellationToken ct)
    {
        var users = (await recipients.GetMedicationAlertRecipientsAsync(r.Elderly, ct)).Distinct().ToArray();
        var keys = users.Select(x => $"elderly-medication-late:{r.MedicationId:N}:{r.ScheduledDate:yyyy-MM-dd}:{r.ScheduledTime:HH\\:mm}:{x.Value:N}").ToArray();
        var existing = (await db.Notifications.Where(x => x.IdempotencyKey != null && keys.Contains(x.IdempotencyKey!)).Select(x => x.IdempotencyKey!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var recipientLanguages = await languages.GetUiLanguagesAsync(users.Select(x => x.Value).ToArray(), ct);
        var created = 0;
        var pending = new List<Notification>();
        foreach (var user in users)
        {
            var key = $"elderly-medication-late:{r.MedicationId:N}:{r.ScheduledDate:yyyy-MM-dd}:{r.ScheduledTime:HH\\:mm}:{user.Value:N}";
            if (!existing.Add(key)) continue;
            // F8: the copy is chosen per recipient from the batch language lookup.
            var copy = NotificationTextCatalog.MedicationDoseMissed(
                NotificationTextCatalog.ResolveLanguage(recipientLanguages, user.Value));
            var notification = Notification.Create(user.Value, "MedicationReminders", "MedicationDoseMissed", copy.Title, copy.Body, "Medication", r.MedicationId, r.CreatedOnUtc, key);
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
                if (db is DbContext context)
                {
                    foreach (var notification in pending)
                        context.Entry(notification).State = EntityState.Detached;
                }

                var committedKeys = await db.Notifications
                    .Where(x => x.IdempotencyKey != null && keys.Contains(x.IdempotencyKey!))
                    .Select(x => x.IdempotencyKey!)
                    .ToListAsync(ct);
                if (committedKeys.ToHashSet(StringComparer.Ordinal).Count != keys.Length)
                    throw;

                return 0;
            }
        }
        return created;
    }
}

public sealed record CreateSosAlertNotificationsCommand(
    Abstractions.Recipients.ElderlyRecipient Elderly, Guid SosId, DateTime CreatedOnUtc) : ICommand<int>;

public sealed class CreateSosAlertNotificationsCommandHandler(
    INotificationsDbContext db, INotificationRecipientGateway recipients, INotificationLanguageGateway languages)
    : ICommandHandler<CreateSosAlertNotificationsCommand, int>
{
    public async Task<Result<int>> Handle(CreateSosAlertNotificationsCommand r, CancellationToken ct)
    {
        var users = (await recipients.GetSosAlertRecipientsAsync(r.Elderly, ct)).Distinct().ToArray();
        var keys = users.Select(x => $"elderly-sos:{r.SosId:N}:{x.Value:N}").ToArray();
        var existing = (await db.Notifications.Where(x => x.IdempotencyKey != null && keys.Contains(x.IdempotencyKey!))
            .Select(x => x.IdempotencyKey!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var recipientLanguages = await languages.GetUiLanguagesAsync(users.Select(x => x.Value).ToArray(), ct);
        var created = 0;
        var pending = new List<Notification>();
        foreach (var user in users)
        {
            var key = $"elderly-sos:{r.SosId:N}:{user.Value:N}";
            if (!existing.Add(key)) continue;
            // F8: the copy is chosen per recipient from the batch language lookup.
            var copy = NotificationTextCatalog.SosAlert(
                NotificationTextCatalog.ResolveLanguage(recipientLanguages, user.Value));
            var notification = Notification.Create(user.Value, "ElderlySos", "SosCreated", copy.Title, copy.Body, "ElderlySos", r.SosId, r.CreatedOnUtc, key);
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
                if (db is DbContext context)
                {
                    foreach (var notification in pending)
                        context.Entry(notification).State = EntityState.Detached;
                }

                var committedKeys = await db.Notifications
                    .Where(x => x.IdempotencyKey != null && keys.Contains(x.IdempotencyKey!))
                    .Select(x => x.IdempotencyKey!)
                    .ToListAsync(ct);
                if (committedKeys.ToHashSet(StringComparer.Ordinal).Count != keys.Length)
                    throw;

                return 0;
            }
        }
        return created;
    }
}
