using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;

namespace Sanad.API.Controllers;

/// <summary>
/// Dedicated caregiver name+id lookup for SuperAdmin/SupportAdmin
/// (<see cref="AuthorizationPolicies.CaregiverReviewAdmin"/>, the policy the
/// caregiver review surface already uses). It closes the gap where the admin
/// caregiver list was the only name search and it also publishes
/// <c>phoneNumber</c>: this route returns names and the caregiver id only, so
/// phone lookup stays exactly where it is today (the SuperAdmin-only admin user
/// list) and no new contact permission is granted. FinanceAdmin stays denied by
/// the policy.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverReviewAdmin)]
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
