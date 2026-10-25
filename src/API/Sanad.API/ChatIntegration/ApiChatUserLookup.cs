using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.API.ChatIntegration;

/// <summary>
/// API-layer gateway: wraps Identity's GetUserProfilesByIdsQuery so the
/// Chat Application only sees <see cref="IChatUserLookup"/> (mirrors
/// FamilyIdentityGateway). displayName prefers the English full name and
/// falls back to Arabic when it is empty.
/// <c>CurrentAccountTypes</c> carries every account on the current user row
/// (eligibility input); the display <c>AccountType</c> stays the
/// first-registered integer and must never drive eligibility. Non-active
/// users (suspended/blocked) resolve as unknown: no chat, no history, no
/// realtime for disabled accounts.
/// </summary>
public sealed class ApiChatUserLookup : IChatUserLookup
{
    private readonly ISender _sender;
    private readonly IIdentityDbContext? _identity;

    public ApiChatUserLookup(ISender sender, IIdentityDbContext? identity = null)
    {
        _sender = sender;
        _identity = identity;
    }

    public async Task<UserProfile?> GetProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == UserId.Empty)
        {
            return null;
        }

        var result = await _sender.Send(
            new GetUserProfilesByIdsQuery([userId]),
            cancellationToken);

        if (result.IsFailure)
        {
            return null;
        }

        UserProfileById? profile = result.Value.FirstOrDefault();

        if (profile is null)
        {
            return null;
        }

        if (profile.Status != (int)UserStatus.Active)
        {
            return null;
        }

        string displayName =
            string.IsNullOrWhiteSpace(profile.EnglishFullName)
                ? profile.ArabicFullName
                : profile.EnglishFullName;

        IReadOnlyList<int>? currentTypes = null;

        if (_identity is not null)
        {
            IReadOnlyDictionary<UserId, IReadOnlyList<int>?> types =
                await GetCurrentAccountTypesAsync([userId], cancellationToken);
            types.TryGetValue(userId, out currentTypes);
        }

        return new UserProfile(
            profile.UserId,
            displayName,
            // Avatar parity (#34): the raw identity storage key never crosses
            // the module boundary — no authorized cross-user route exists, and
            // keeping the key here invites a future re-leak seam.
            null,
            profile.AccountType,
            currentTypes,
            // Bilingual names (C1): the stored names as registered; an empty
            // stored name stays null so the client falls back to displayName.
            NullIfBlank(profile.ArabicFullName),
            NullIfBlank(profile.EnglishFullName));
    }

    /// <summary>
    /// True batch: one profile round-trip plus one account-type round-trip
    /// per page instead of per conversation. Every requested id gets an
    /// entry (null when unknown or non-active).
    /// </summary>
    public async Task<IReadOnlyDictionary<UserId, UserProfile?>> GetProfilesAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        UserId[] distinct = userIds
            .Where(id => id != UserId.Empty)
            .Distinct()
            .ToArray();

        var profiles = new Dictionary<UserId, UserProfile?>();

        if (distinct.Length == 0)
        {
            return profiles;
        }

        var result = await _sender.Send(
            new GetUserProfilesByIdsQuery(distinct),
            cancellationToken);

        Dictionary<Guid, UserProfileById> byId = result.IsFailure
            ? new Dictionary<Guid, UserProfileById>()
            : result.Value
                .Where(profile => profile.Status == (int)UserStatus.Active)
                .GroupBy(profile => profile.UserId.Value)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());

        IReadOnlyDictionary<UserId, IReadOnlyList<int>?> accountTypes =
            await GetCurrentAccountTypesAsync(distinct, cancellationToken);

        foreach (UserId userId in distinct)
        {
            if (!byId.TryGetValue(userId.Value, out UserProfileById? profile))
            {
                profiles[userId] = null;
                continue;
            }

            string displayName =
                string.IsNullOrWhiteSpace(profile.EnglishFullName)
                    ? profile.ArabicFullName
                    : profile.EnglishFullName;

            accountTypes.TryGetValue(userId, out IReadOnlyList<int>? types);

            profiles[userId] = new UserProfile(
                profile.UserId,
                displayName,
                // Avatar parity (#34): the raw identity storage key never
                // crosses the module boundary.
                null,
                profile.AccountType,
                types,
                // Bilingual names (C1): stored names as registered; an empty
                // stored name stays null.
                NullIfBlank(profile.ArabicFullName),
                NullIfBlank(profile.EnglishFullName));
        }

        return profiles;
    }

    /// <summary>An absent or blank stored name is null, never an empty string.</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<IReadOnlyDictionary<UserId, IReadOnlyList<int>?>> GetCurrentAccountTypesAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var empty = new Dictionary<UserId, IReadOnlyList<int>?>();

        if (_identity is null || userIds.Count == 0)
        {
            return empty;
        }

        UserId[] distinct = userIds.Distinct().ToArray();

        // Whole-property projection; .Value never appears in the query.
        var rows = await _identity.Users
            .AsNoTracking()
            .Where(user => distinct.Contains(user.Id))
            .Select(user => new
            {
                user.Id,
                user.Accounts
            })
            .ToListAsync(cancellationToken);

        var types = new Dictionary<UserId, IReadOnlyList<int>?>();

        foreach (var row in rows)
        {
            types[row.Id] = row.Accounts
                .Select(account => (int)account.AccountType)
                .Distinct()
                .ToArray();
        }

        return types;
    }
}
