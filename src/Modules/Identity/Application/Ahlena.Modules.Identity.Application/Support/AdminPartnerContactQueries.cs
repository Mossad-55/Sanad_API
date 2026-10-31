using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Support;

namespace Ahlena.Modules.Identity.Application.Support;

/// <summary>
/// One partner-contact row in the admin support inbox. The submitter's own
/// name, company and email are **request data** and are shown as submitted;
/// the anti-spam client key is never exposed.
/// </summary>
public sealed record AdminPartnerContactListItem(
    Guid ContactId,
    string Name,
    string? Company,
    string? Phone,
    string? CompanyType,
    string? City,
    string Email,
    string? Subject,
    DateTime CreatedOnUtc);

public sealed record AdminPartnerContactsPage(
    IReadOnlyList<AdminPartnerContactListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// One partner contact with its message. Same submitted facts as the list row
/// plus the message body.
/// </summary>
public sealed record AdminPartnerContactDetail(
    Guid ContactId,
    string Name,
    string? Company,
    string? Phone,
    string? CompanyType,
    string? City,
    string Email,
    string? Subject,
    string Message,
    DateTime CreatedOnUtc);

public sealed record GetAdminPartnerContactsQuery(
    int Page = 1,
    int PageSize = 25) : IQuery<AdminPartnerContactsPage>;

public sealed class GetAdminPartnerContactsQueryHandler(
    IIdentityDbContext dbContext)
    : IQueryHandler<
        GetAdminPartnerContactsQuery,
        AdminPartnerContactsPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminPartnerContactsPage>> Handle(
        GetAdminPartnerContactsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize,
            1,
            MaxPageSize);

        var query = dbContext.PartnerContacts.AsNoTracking();

        int totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(contact => contact.CreatedOnUtc)
            .ThenByDescending(contact => contact.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(contact => new AdminPartnerContactListItem(
                contact.Id.Value,
                contact.Name,
                contact.Company,
                contact.Phone,
                contact.CompanyType,
                contact.City,
                contact.Email,
                contact.Subject,
                contact.CreatedOnUtc))
            .ToList();

        return new AdminPartnerContactsPage(
            items,
            page,
            pageSize,
            totalCount);
    }
}

public sealed record GetAdminPartnerContactByIdQuery(
    Guid ContactId) : IQuery<AdminPartnerContactDetail>;

public sealed class GetAdminPartnerContactByIdQueryHandler(
    IIdentityDbContext dbContext)
    : IQueryHandler<
        GetAdminPartnerContactByIdQuery,
        AdminPartnerContactDetail>
{
    public async Task<Result<AdminPartnerContactDetail>> Handle(
        GetAdminPartnerContactByIdQuery request,
        CancellationToken cancellationToken)
    {
        PartnerContact? contact = await dbContext.PartnerContacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == new PartnerContactId(request.ContactId),
                cancellationToken);

        if (contact is null)
        {
            return Result<AdminPartnerContactDetail>.Failure(
                PartnerContactErrors.NotFound);
        }

        return new AdminPartnerContactDetail(
            contact.Id.Value,
            contact.Name,
            contact.Company,
            contact.Phone,
            contact.CompanyType,
            contact.City,
            contact.Email,
            contact.Subject,
            contact.Message,
            contact.CreatedOnUtc);
    }
}
