using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;

namespace Sanad.API.Controllers;

/// <summary>
/// Dedicated caregiver name+id lookup for SuperAdmin/FinanceAdmin
/// (<see cref="AuthorizationPolicies.CaregiverLookupAdmin"/>, a lookup-only
/// policy implementing the agreed screen-to-role matrix). It closes the gap
/// where the admin caregiver list was the only name search and it publishes
/// names and the caregiver id only, so phone lookup stays exactly where it is
/// today (the SuperAdmin-only admin user list) and no new contact permission
/// is granted. SupportAdmin is explicitly removed from this lookup (prior
/// access through the shared review policy); the caregiver review and ratings
/// surfaces keep the shared <see cref="AuthorizationPolicies.CaregiverReviewAdmin"/>
/// policy unchanged. FinanceAdmin had no prior lookup access and gains it
/// here only; ContentAdmin and every app audience are denied.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverLookupAdmin)]
[Route("api/v1/admin/caregivers")]
public sealed class AdminCaregiverLookupController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Bounded names+id page. <c>search</c> is an optional case-insensitive
    /// substring of the Arabic or English full name over the caregiver register
    /// (caregivers whose Identity user is missing are not returned). Paging is
    /// clamped (page at least 1, page size default 20, maximum 100).
    /// </summary>
    [HttpGet("lookup")]
    [ProducesResponseType(
        typeof(PagedResult<AdminCaregiverLookupItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new LookupAdminCaregiversQuery(search, page, pageSize),
            cancellationToken));
}
