using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.HelpRequests;
namespace Ahlena.Modules.Families.Application.Abstractions.HelpRequests;
public sealed record HelpRequestCatalogItem(string Key, SentenceBuilderCatalogCategory Category, string ArabicLabel, string EnglishLabel);
public enum SentenceBuilderCatalogCategory { Actor = 1, Action = 2, Need = 3, Qualifier = 4 }
public interface IHelpRequestCatalogGateway { Task<IReadOnlyList<HelpRequestCatalogItem>> GetActiveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default); }
