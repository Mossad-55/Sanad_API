using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;

namespace Ahlena.Modules.Cms.Application.Help;

public sealed record PublicSupportContactResponse(
    string? SupportPhone,
    string? SupportEmail);

public sealed record GetPublicSupportContactQuery() : IQuery<PublicSupportContactResponse?>;

public sealed class GetPublicSupportContactQueryHandler(ICmsDbContext dbContext)
    : IQueryHandler<GetPublicSupportContactQuery, PublicSupportContactResponse?>
{
    public async Task<Result<PublicSupportContactResponse?>> Handle(
        GetPublicSupportContactQuery request,
        CancellationToken cancellationToken)
    {
        var contact = await dbContext.SupportContacts
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (contact is null)
        {
            return new PublicSupportContactResponse(null, null);
        }

        return new PublicSupportContactResponse(contact.SupportPhone, contact.SupportEmail);
    }
}