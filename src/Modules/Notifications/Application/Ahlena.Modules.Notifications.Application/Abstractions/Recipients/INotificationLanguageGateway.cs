using Ahlena.BuildingBlocks.Domain.Enums;

namespace Ahlena.Modules.Notifications.Application.Abstractions.Recipients;

/// <summary>
/// Read seam for the persisted UI language of notification recipients. The
/// lookup is batch-shaped on purpose: a fan-out handler resolves every
/// recipient of one event in a single round trip instead of one query per row.
/// A recipient with no persisted language is simply absent from the result;
/// callers fall back to the documented Arabic default through
/// <see cref="Ahlena.Modules.Notifications.Application.Notifications.NotificationTextCatalog.ResolveLanguage"/>.
/// </summary>
public interface INotificationLanguageGateway
{
    Task<IReadOnlyDictionary<Guid, UiLanguage>> GetUiLanguagesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
