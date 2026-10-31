using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;

namespace Ahlena.API.NotificationsIntegration;

/// <summary>
/// F8 read seam for the persisted UI language of notification recipients. One
/// batch query answers a whole fan-out; recipients without a persisted row are
/// simply absent from the result and the caller keeps the documented Arabic
/// default. Recipient eligibility and delivery preferences stay with the
/// existing recipient gateway — this seam only answers which language the copy
/// for a recipient is written in.
/// </summary>
public sealed class NotificationLanguageGateway(IdentityDbContext identity) : INotificationLanguageGateway
{
    /// <summary>Server-side recipient language row with a strongly-typed id.</summary>
    public sealed record UserLanguageRow(UserId Id, UiLanguage UiLanguage);

    /// <summary>
    /// Provider-safe language lookup. Strongly-typed ids are compared and
    /// projected directly so the EF value converter translates on PostgreSQL;
    /// the <c>.Value</c> unwrap happens client-side after materialization.
    /// Member access such as <c>x.Id.Value</c> inside the query predicate is
    /// not translatable and must not be reintroduced here.
    /// </summary>
    public static IQueryable<UserLanguageRow> UserLanguageRows(IdentityDbContext identity, UserId[] targets) =>
        identity.Users.AsNoTracking().Where(x => targets.Contains(x.Id))
            .Select(x => new UserLanguageRow(x.Id, x.UiLanguage));

    public async Task<IReadOnlyDictionary<Guid, UiLanguage>> GetUiLanguagesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, UiLanguage>();

        UserId[] targets = userIds.Distinct().Select(userId => new UserId(userId)).ToArray();
        var rows = await UserLanguageRows(identity, targets).ToListAsync(cancellationToken);

        var languages = new Dictionary<Guid, UiLanguage>(rows.Count);
        foreach (var row in rows)
            languages[row.Id.Value] = row.UiLanguage;

        return languages;
    }
}
