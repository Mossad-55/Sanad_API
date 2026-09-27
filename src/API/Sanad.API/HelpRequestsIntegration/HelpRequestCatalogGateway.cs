using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.SentenceBuilder;
using Sanad.Modules.Families.Application.Abstractions.HelpRequests;
namespace Sanad.API.HelpRequestsIntegration;
public sealed class HelpRequestCatalogGateway(ICmsDbContext db) : IHelpRequestCatalogGateway
{ public async Task<IReadOnlyList<HelpRequestCatalogItem>> GetActiveAsync(IEnumerable<string> keys, CancellationToken ct = default) { var wanted = keys.Distinct().ToArray(); return await db.SentenceBuilderCatalogEntries.AsNoTracking().Where(x => wanted.Contains(x.StableKey)).SelectMany(x => x.Revisions.Where(r => r.IsActive).Select(r => new HelpRequestCatalogItem(x.StableKey, (SentenceBuilderCatalogCategory)x.Category, r.ArabicLabel, r.EnglishLabel))).ToListAsync(ct); } }
