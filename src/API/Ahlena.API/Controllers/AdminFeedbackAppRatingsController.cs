using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.Modules.Identity.Application.Feedback;

namespace Ahlena.API.Controllers;

/// <summary>
/// Admin app-rating investigation read (portal second pass B3): a paginated,
/// read-only list of the ratings the authenticated app submission stored.
/// SuperAdmin and SupportAdmin on a Normal token only (OperationalReadAdmin);
/// ContentAdmin, FinanceAdmin and every app audience are deliberately denied.
/// The response projects the stored columns only — the internal user id is the
/// sole attribution, and no name, phone, session, or contact data is joined in.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.OperationalReadAdmin)]
[Route("api/v1/admin/feedback/app-ratings")]
public sealed class AdminFeedbackAppRatingsController : ApiControllerBase
{
    private readonly ISender _sender;

    public AdminFeedbackAppRatingsController(ISender sender) => _sender = sender;

    /// <summary>Stored app ratings, newest first: { items, page, pageSize, totalCount }.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedAdminAppRatings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListAppRatings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok((await _sender.Send(new GetAdminAppRatingsQuery(page, pageSize), cancellationToken)).Value);
}
