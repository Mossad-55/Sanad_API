using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using System.Collections.Generic;

namespace Ahlena.Modules.Community.Application.HelpRequests;

public sealed record GetElderlyHelpRequestCatalogQuery() : IQuery<IReadOnlyList<HelpRequestCatalogItem>>;

public sealed record HelpRequestCatalogItem
{
    public string Key { get; init; } = string.Empty;
    public string ArabicLabel { get; init; } = string.Empty;
    public string EnglishLabel { get; init; } = string.Empty;
}
