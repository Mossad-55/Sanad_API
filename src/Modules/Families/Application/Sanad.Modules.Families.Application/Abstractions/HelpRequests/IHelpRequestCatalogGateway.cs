using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.HelpRequests;
namespace Sanad.Modules.Families.Application.Abstractions.HelpRequests;
public sealed record HelpRequestCatalogItem(string Key, SentenceBuilderCatalogCategory Category, string ArabicLabel, string EnglishLabel);
public enum SentenceBuilderCatalogCategory { Actor = 1, Action = 2, Need = 3, Qualifier = 4 }
public interface IHelpRequestCatalogGateway { Task<IReadOnlyList<HelpRequestCatalogItem>> GetActiveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default); }
