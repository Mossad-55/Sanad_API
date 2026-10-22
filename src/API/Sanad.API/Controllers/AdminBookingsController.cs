using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.API.Controllers;

public sealed record AdminRecordBookingOutcomeRequest(
    int OutcomeReason,
    string? Reason,
    int? ReasonCategory,
    int? ProviderLatenessMinutes = null,
    bool CustomerConsentedToLateness = false,
    bool ForceMajeure = false);

public sealed record AdminReconcileRefundAmbiguityRequest(
    string Evidence,
    bool RefundDidMove,
    Guid? ExpectedAttemptClaimId = null,
    string? VerifiedProviderReference = null);

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin/bookings")]
public sealed class AdminBookingsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AdminBookingsController(
        ISender sender,
        IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _dateTimeProvider = dateTimeProvider;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedAdminBookings), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBookings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] AdminBookingFinanceFilter finance = AdminBookingFinanceFilter.All,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListAdminBookingsQuery(page, pageSize, finance),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("cancellations")]
    [ProducesResponseType(typeof(PagedAdminCancellations), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCancellations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] BookingCancellationActorSide? actor = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListAdminCancellationsQuery(page, pageSize, actor),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBooking(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetAdminBookingDetailQuery(new BookingId(bookingId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{bookingId:guid}/refund")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundBooking(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new AdminRefundBookingCommand(
                new BookingId(bookingId),
                _dateTimeProvider.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Records a platform-asserted provider outcome on an accepted booking: provider lateness
    /// beyond the threshold, an inaccessible recipient, a safety cancellation, or a force-majeure
    /// event. The refund is decided by the booking's sold policy revision and settled to the
    /// payment provider.
    /// </summary>
    [HttpPost("{bookingId:guid}/outcome")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RecordOutcome(
        Guid bookingId,
        [FromBody] AdminRecordBookingOutcomeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new AdminRecordBookingOutcomeCommand(
                userId,
                new BookingId(bookingId),
                request.OutcomeReason,
                request.Reason ?? string.Empty,
                request.ReasonCategory,
                request.ProviderLatenessMinutes,
                request.CustomerConsentedToLateness,
                request.ForceMajeure,
                _dateTimeProvider.UtcNow),
            cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Reconciles a refund attempt whose provider outcome could not be confirmed (Owner decision
    /// #17). This is the only command that clears the booking's ambiguity marker, and until it
    /// runs every refund path on that booking is refused. The operator states what they found —
    /// whether the money actually moved — and that finding, not a guess, books the amount.
    /// </summary>
    [HttpPost("{bookingId:guid}/refund/reconcile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReconcileRefundAmbiguity(
        Guid bookingId,
        [FromBody] AdminReconcileRefundAmbiguityRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new ReconcileBookingRefundAmbiguityCommand(
                userId,
                new BookingId(bookingId),
                request.Evidence,
                request.RefundDidMove,
                _dateTimeProvider.UtcNow,
                request.ExpectedAttemptClaimId,
                request.VerifiedProviderReference),
            cancellationToken);

        return ToActionResult(result);
    }
}
