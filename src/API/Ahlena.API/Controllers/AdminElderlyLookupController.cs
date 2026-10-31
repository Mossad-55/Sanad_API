using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.Modules.Families.Application.Families;

namespace Ahlena.API.Controllers;

/// <summary>
/// Dedicated dependent (elderly) name+id lookup for SuperAdmin/SupportAdmin
/// (<see cref="AuthorizationPolicies.OperationalReadAdmin"/>, the read-only
/// operational policy shared with the family register). The route exists so an
/// operator can resolve a pasted dependent name to the single dependent id used
/// by care records, check-ins, SOS, help requests and medications without
/// widening the family-detail payload or reading any contact or medical value.
/// FinanceAdmin is deliberately not in the policy: Finance payout reads are a
/// separate surface and are not widened here.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.OperationalReadAdmin)]
[Route("api/v1/admin/elderly")]
public sealed class AdminElderlyLookupController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Bounded names+id page. <c>search</c> is an optional case-insensitive
    /// substring of the Arabic or English full name and is never an error: a
    /// blank search and a search that matches nothing both return a page.
    /// Paging is clamped (page at least 1, page size default 20, maximum 100).
    /// </summary>
    [HttpGet("lookup")]
    [ProducesResponseType(
        typeof(AdminElderlyLookupPage),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new LookupAdminElderliesQuery(search, page, pageSize),
            cancellationToken));
}
