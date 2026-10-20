using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;

namespace Sanad.Modules.Cms.Application.Help;

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