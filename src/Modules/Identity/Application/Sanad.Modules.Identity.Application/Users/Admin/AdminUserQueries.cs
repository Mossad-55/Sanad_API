using FluentValidation;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users.Admin;

public static class AdminUserErrors
{
    public static Error InvalidQuery => new("Identity.Admin.InvalidQuery", "The admin user query is invalid.");
    public static Error NotFound => new("Identity.User.NotFound", "The user was not found.");
}

public sealed record AdminUserListQuery(
    string? Search,
    string? AccountType,
    string? Status,
    string? Sort,
    int Page,
    int PageSize) : IQuery<AdminUserListResponse>;

public sealed record AdminUserDetailQuery(UserId UserId) : IQuery<AdminUserDetailResponse>;

public sealed record AdminSessionOverviewQuery(
    string? AccountType,
    string? UserId,
    string? Sort,
    int Page,
    int PageSize) : IQuery<AdminSessionOverviewResponse>;

public enum AdminUserSort { Newest, Oldest, Name }

public sealed record AdminUserListResponse(
    IReadOnlyList<AdminUserListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record AdminUserListItem(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string? Email,
    string Phone,
    UserStatus Status,
    IReadOnlyList<AccountType> AccountTypes,
    DateTime CreatedOnUtc,
    bool IsVerified);

public sealed record AdminUserDetailResponse(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string? Email,
    string Phone,
    UserStatus Status,
    IReadOnlyList<AccountType> AccountTypes,
    DateTime CreatedOnUtc,
    bool IsVerified,
    IReadOnlyList<AdminSessionItem> Sessions,
    AdminLinkedFacility Linked);

public sealed record AdminSessionItem(
    Guid DeviceId,
    string DeviceName,
    DevicePlatform Platform,
    string AppVersion,
    DateTime CreatedOnUtc,
    DateTime ExpiresOnUtc,
    DateTime? LastSeenUtc);

public sealed record AdminLinkedFacility(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? FacilityId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FacilityStatus)
{
    public static AdminLinkedFacility Empty { get; } = new(null, null);
}

public sealed record AdminSessionOverviewResponse(
    IReadOnlyList<AdminSessionOverviewItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record AdminSessionOverviewItem(
    Guid UserId,
    string ArabicName,
    string EnglishName,
    IReadOnlyList<AccountType> AccountTypes,
    AdminSessionItem Session);

public sealed record CareHomeOwnerFacility(Guid FacilityId, string FacilityStatus);

public interface ICareHomeOwnerFacilityLookup
{
    Task<CareHomeOwnerFacility?> FindAsync(UserId ownerUserId, CancellationToken cancellationToken);
}

public static class AdminQueryParsing
{
    public static bool TryPage(int page, int pageSize, out int normalizedPage, out int normalizedPageSize)
    {
        normalizedPage = Math.Clamp(page, 1, 100);
        normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        return true;
    }

    public static bool TryEnum<T>(string? value, out T? result) where T : struct, Enum
    {
        result = null;
        return string.IsNullOrWhiteSpace(value) ||
            (Enum.TryParse(value, true, out T parsed) && Enum.IsDefined(parsed) && (result = parsed) is not null);
    }

    public static bool TrySort(string? value, out AdminUserSort sort)
    {
        sort = AdminUserSort.Newest;
        return string.IsNullOrWhiteSpace(value) || Enum.TryParse(value, true, out sort) && Enum.IsDefined(sort);
    }
}

public sealed class AdminUserListQueryValidator : AbstractValidator<AdminUserListQuery>
{
    public AdminUserListQueryValidator()
    {
        RuleFor(x => x).Must(x => AdminQueryParsing.TryPage(x.Page, x.PageSize, out _, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
        RuleFor(x => x).Must(x => AdminQueryParsing.TrySort(x.Sort, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
        RuleFor(x => x).Must(x => AdminQueryParsing.TryEnum<AccountType>(x.AccountType, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
        RuleFor(x => x).Must(x => AdminQueryParsing.TryEnum<UserStatus>(x.Status, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
    }
}

public sealed class AdminSessionOverviewQueryValidator : AbstractValidator<AdminSessionOverviewQuery>
{
    public AdminSessionOverviewQueryValidator()
    {
        RuleFor(x => x).Must(x => AdminQueryParsing.TryPage(x.Page, x.PageSize, out _, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
        RuleFor(x => x).Must(x => AdminQueryParsing.TrySort(x.Sort, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
        RuleFor(x => x).Must(x => AdminQueryParsing.TryEnum<AccountType>(x.AccountType, out _)).WithErrorCode("Identity.Admin.InvalidQuery");
        RuleFor(x => x).Must(x => string.IsNullOrWhiteSpace(x.UserId) || Guid.TryParse(x.UserId, out var id) && id != Guid.Empty).WithErrorCode("Identity.Admin.InvalidQuery");
    }
}

public sealed class AdminUserListQueryHandler(IIdentityDbContext db)
    : IQueryHandler<AdminUserListQuery, AdminUserListResponse>
{
    public async Task<Result<AdminUserListResponse>> Handle(AdminUserListQuery request, CancellationToken ct)
    {
        if (!AdminQueryParsing.TryPage(request.Page, request.PageSize, out var page, out var pageSize) ||
            !AdminQueryParsing.TrySort(request.Sort, out var sort) ||
            !AdminQueryParsing.TryEnum<AccountType>(request.AccountType, out var accountType) ||
            !AdminQueryParsing.TryEnum<UserStatus>(request.Status, out var status))
            return AdminUserErrors.InvalidQuery;

        IQueryable<User> query = db.Users.AsNoTracking();
        if (accountType is not null) query = query.Where(user => user.Accounts.Any(account => account.AccountType == accountType));
        if (status is not null) query = query.Where(user => user.Status == status);

        // These fields are value objects persisted through EF value converters. EF cannot
        // translate their string members (or string ordering) for SQLite/relational providers.
        // Apply the translatable filters first, then evaluate only these two operations in memory.
        if (!string.IsNullOrWhiteSpace(request.Search) || sort == AdminUserSort.Name)
        {
            List<User> candidates = await query.Include(user => user.Accounts).ToListAsync(ct);
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                string term = request.Search.Trim();
                candidates = candidates.Where(user =>
                    ContainsIgnoreCase(user.ArabicFullName.Value, term) ||
                    ContainsIgnoreCase(user.EnglishFullName.Value, term) ||
                    (user.Email is not null && ContainsIgnoreCase(user.Email.Value, term)) ||
                    ContainsIgnoreCase(user.PhoneNumber.Value, term)).ToList();
            }
            IEnumerable<User> matched = sort switch
            {
                AdminUserSort.Oldest => candidates.OrderBy(user => user.CreatedOnUtc),
                AdminUserSort.Name => candidates.OrderBy(user => user.ArabicFullName.Value, StringComparer.Ordinal)
                    .ThenBy(user => user.EnglishFullName.Value, StringComparer.Ordinal).ThenBy(user => user.Id.Value),
                _ => candidates.OrderByDescending(user => user.CreatedOnUtc)
            };
            User[] pageOfUsers = matched.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            return new AdminUserListResponse(pageOfUsers.Select(MapList).ToArray(), page, pageSize, candidates.Count);
        }
        query = ApplySort(query, sort);
        int total = await query.CountAsync(ct);
        List<User> users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new AdminUserListResponse(users.Select(MapList).ToArray(), page, pageSize, total);
    }

    internal static IQueryable<User> ApplySort(IQueryable<User> query, AdminUserSort sort) => sort switch
    {
        AdminUserSort.Oldest => query.OrderBy(user => user.CreatedOnUtc),
        AdminUserSort.Name => query.OrderBy(user => EF.Property<string>(user, nameof(User.ArabicFullName))).ThenBy(user => EF.Property<string>(user, nameof(User.EnglishFullName))).ThenBy(user => user.Id),
        _ => query.OrderByDescending(user => user.CreatedOnUtc)
    };

    internal static AdminUserListItem MapList(User user) => new(user.Id.Value, user.ArabicFullName.Value, user.EnglishFullName.Value, user.Email?.Value, user.PhoneNumber.Value, user.Status, user.Accounts.Select(x => x.AccountType).OrderBy(x => x).ToArray(), user.CreatedOnUtc, user.Status != UserStatus.PendingVerification);

    private static bool ContainsIgnoreCase(string value, string term) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
}

public sealed class AdminUserDetailQueryHandler(IIdentityDbContext db, ICareHomeOwnerFacilityLookup facilities, IDateTimeProvider clock)
    : IQueryHandler<AdminUserDetailQuery, AdminUserDetailResponse>
{
    public async Task<Result<AdminUserDetailResponse>> Handle(AdminUserDetailQuery request, CancellationToken ct)
    {
        if (request.UserId == UserId.Empty) return AdminUserErrors.NotFound;
        User? user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.UserId, ct);
        if (user is null) return AdminUserErrors.NotFound;
        DateTime now = clock.UtcNow;
        List<AdminSessionItem> sessions = await db.DeviceSessions.AsNoTracking()
            .Where(x => x.UserId == request.UserId && x.RevokedOnUtc == null && x.ExpiresOnUtc > now)
            .OrderBy(x => x.CreatedOnUtc)
            .Select(x => new AdminSessionItem(x.Id.Value, x.DeviceName, x.Platform, x.AppVersion, x.CreatedOnUtc, x.ExpiresOnUtc, x.LastRotatedOnUtc))
            .ToListAsync(ct);
        AdminLinkedFacility linked = AdminLinkedFacility.Empty;
        if (user.Accounts.Any(x => x.AccountType == AccountType.CareHomeOwner))
        {
            CareHomeOwnerFacility? facility = await facilities.FindAsync(request.UserId, ct);
            if (facility is not null) linked = new(facility.FacilityId, facility.FacilityStatus);
        }
        return new AdminUserDetailResponse(user.Id.Value, user.ArabicFullName.Value, user.EnglishFullName.Value, user.Email?.Value, user.PhoneNumber.Value, user.Status, user.Accounts.Select(x => x.AccountType).OrderBy(x => x).ToArray(), user.CreatedOnUtc, user.Status != UserStatus.PendingVerification, sessions, linked);
    }
}

public sealed class AdminSessionOverviewQueryHandler(IIdentityDbContext db, IDateTimeProvider clock)
    : IQueryHandler<AdminSessionOverviewQuery, AdminSessionOverviewResponse>
{
    private static readonly AccountType[] AdminTypes = [AccountType.SuperAdmin, AccountType.SupportAdmin, AccountType.ContentAdmin, AccountType.FinanceAdmin];

    public async Task<Result<AdminSessionOverviewResponse>> Handle(AdminSessionOverviewQuery request, CancellationToken ct)
    {
        if (!AdminQueryParsing.TryPage(request.Page, request.PageSize, out var page, out var pageSize) ||
            !AdminQueryParsing.TrySort(request.Sort, out var sort) ||
            !AdminQueryParsing.TryEnum<AccountType>(request.AccountType, out var accountType) ||
            (!string.IsNullOrWhiteSpace(request.UserId) && (!Guid.TryParse(request.UserId, out var userGuid) || userGuid == Guid.Empty)))
            return AdminUserErrors.InvalidQuery;
        DateTime now = clock.UtcNow;
        Guid? userId = string.IsNullOrWhiteSpace(request.UserId) ? null : Guid.Parse(request.UserId);
        IQueryable<User> users = db.Users.AsNoTracking().Where(user => user.Accounts.Any(a => AdminTypes.Contains(a.AccountType)));
        if (accountType is not null) users = users.Where(user => user.Accounts.Any(a => a.AccountType == accountType));
        if (userId is not null) users = users.Where(user => user.Id == new UserId(userId.Value));
        IQueryable<DeviceSession> sessions = db.DeviceSessions.AsNoTracking().Where(session => session.RevokedOnUtc == null && session.ExpiresOnUtc > now && users.Select(user => user.Id).Contains(session.UserId));
        var query = from session in sessions join user in users on session.UserId equals user.Id select new { session, user };
        if (sort == AdminUserSort.Name)
        {
            var candidates = await query.Select(x => new { x.session, x.user }).ToListAsync(ct);
            var ordered = candidates.OrderBy(x => x.user.ArabicFullName.Value, StringComparer.Ordinal)
                .ThenBy(x => x.user.EnglishFullName.Value, StringComparer.Ordinal)
                .ThenBy(x => x.user.Id.Value).ThenBy(x => x.session.Id.Value);
            var nameSortedRows = ordered.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new AdminSessionOverviewItem(x.user.Id.Value, x.user.ArabicFullName.Value,
                    x.user.EnglishFullName.Value, x.user.Accounts.Select(a => a.AccountType).OrderBy(a => a).ToArray(),
                    new AdminSessionItem(x.session.Id.Value, x.session.DeviceName, x.session.Platform,
                        x.session.AppVersion, x.session.CreatedOnUtc, x.session.ExpiresOnUtc, x.session.LastRotatedOnUtc)))
                .ToArray();
            return new AdminSessionOverviewResponse(nameSortedRows, page, pageSize, candidates.Count);
        }
        query = sort switch
        {
            AdminUserSort.Oldest => query.OrderBy(x => x.session.CreatedOnUtc).ThenBy(x => x.session.Id),
            AdminUserSort.Name => throw new InvalidOperationException("Name ordering is evaluated after materialization."),
            _ => query.OrderByDescending(x => x.session.CreatedOnUtc).ThenBy(x => x.session.Id)
        };
        int total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new AdminSessionOverviewItem(x.user.Id.Value, EF.Property<string>(x.user, nameof(User.ArabicFullName)), EF.Property<string>(x.user, nameof(User.EnglishFullName)), x.user.Accounts.Select(a => a.AccountType).ToArray(), new AdminSessionItem(x.session.Id.Value, x.session.DeviceName, x.session.Platform, x.session.AppVersion, x.session.CreatedOnUtc, x.session.ExpiresOnUtc, x.session.LastRotatedOnUtc))).ToListAsync(ct);
        return new AdminSessionOverviewResponse(rows, page, pageSize, total);
    }
}
