using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ahlena.Modules.Community.Application.HelpRequests;

public sealed class GetElderlyHelpRequestCatalogQueryHandler
    : IQueryHandler<GetElderlyHelpRequestCatalogQuery, IReadOnlyList<HelpRequestCatalogItem>>
{
    public Task<Result<IReadOnlyList<HelpRequestCatalogItem>>> Handle(
        GetElderlyHelpRequestCatalogQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HelpRequestCatalogItem> items = new List<HelpRequestCatalogItem>
        {
            new() { Key = "actor", ArabicLabel = "فاعل", EnglishLabel = "Actor" },
            new() { Key = "action", ArabicLabel = "فعل", EnglishLabel = "Action" },
            new() { Key = "need", ArabicLabel = "need", EnglishLabel = "Need" },
            new() { Key = "qualifier", ArabicLabel = "مؤهل", EnglishLabel = "Qualifier" }
        };
        return Task.FromResult(Result<IReadOnlyList<HelpRequestCatalogItem>>.Success(items));
    }
}
