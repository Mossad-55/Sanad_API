using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.PayoutOperationalAdmin)]
[Route("api/v1/admin/caregiver-payout-accounts")]
public sealed class AdminCaregiverPayoutAccountsController(
    ISender sender,
    IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResult<PayoutAccountAdminItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayoutAccounts(
        [FromQuery] PayoutAccountStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetPayoutAccountsQuery(status, page, pageSize),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}")]
    [ProducesResponseType(
        typeof(PayoutAccountAdminDetailResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayoutAccount(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetPayoutAccountDetailQuery(caregiverId),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}/reviews")]
    [ProducesResponseType(
        typeof(IReadOnlyList<PayoutAccountReviewResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviews(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetPayoutAccountReviewsQuery(caregiverId),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/approve")]
    [ProducesResponseType(
        typeof(PayoutAccountAdminDetailResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(
        Guid caregiverId,
        [FromBody] ApprovePayoutAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new ApprovePayoutAccountCommand(
                caregiverId,
                actor,
                request.ExpectedRevision,
                request.VerificationSource,
                request.Reference,
                clock.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/reject")]
    [ProducesResponseType(
        typeof(PayoutAccountAdminDetailResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(
        Guid caregiverId,
        [FromBody] RejectPayoutAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new RejectPayoutAccountCommand(
                caregiverId,
                actor,
                request.ExpectedRevision,
                request.Reason,
                clock.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/revoke")]
    [ProducesResponseType(
        typeof(PayoutAccountAdminDetailResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(
        Guid caregiverId,
        [FromBody] RevokePayoutAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new RevokePayoutAccountCommand(
                caregiverId,
                actor,
                request.ExpectedRevision,
                request.Reason,
                clock.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/reveal")]
    [ProducesResponseType(
        typeof(IbanRevealResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Reveal(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new RevealPayoutAccountIbanCommand(
                caregiverId,
                actor,
                clock.UtcNow),
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        Response.GetTypedHeaders().CacheControl =
            new Microsoft.Net.Http.Headers.CacheControlHeaderValue
            {
                NoStore = true
            };
        Response.Headers.Pragma = "no-cache";

        return Ok(result.Value);
    }
}
