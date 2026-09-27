using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Cms.Application.SentenceBuilder;
using Sanad.Modules.Cms.Domain.SentenceBuilder;

namespace Sanad.API.Controllers;
[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/sentence-builder/catalog")]
public sealed class AdminSentenceBuilderCatalogController(ISender sender) : ApiControllerBase
{
 [HttpGet] public async Task<IActionResult> List([FromQuery] SentenceBuilderCategory? category = null, [FromQuery] bool? active = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => ToActionResult(await sender.Send(new ListSentenceBuilderCatalogQuery(category, active, page, pageSize), ct));
 [HttpGet("{revisionId:guid}")] public async Task<IActionResult> Detail(Guid revisionId, CancellationToken ct) => ToActionResult(await sender.Send(new GetSentenceBuilderCatalogQuery(revisionId), ct));
 [HttpPost] public async Task<IActionResult> Create([FromBody] CatalogRequest request, CancellationToken ct) => ToActionResult(await sender.Send(new UpsertSentenceBuilderCatalogCommand(request.StableKey, request.Category, request.ArabicLabel, request.EnglishLabel, request.DisplayOrder), ct));
 [HttpPost("{revisionId:guid}/activate")] public async Task<IActionResult> Activate(Guid revisionId, CancellationToken ct) => ToActionResult(await sender.Send(new SetSentenceBuilderCatalogActiveCommand(revisionId, true), ct));
 [HttpPost("{revisionId:guid}/deactivate")] public async Task<IActionResult> Deactivate(Guid revisionId, CancellationToken ct) => ToActionResult(await sender.Send(new SetSentenceBuilderCatalogActiveCommand(revisionId, false), ct));
 public sealed record CatalogRequest(string StableKey, SentenceBuilderCategory Category, string ArabicLabel, string EnglishLabel, int DisplayOrder);
}
