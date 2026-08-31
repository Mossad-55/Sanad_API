using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin/caregivers")]
public sealed class AdminCaregiversController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminCaregiversController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("{caregiverId:guid}/certificates/{certificateId:guid}/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyCertificate(
        Guid caregiverId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new VerifyCertificateCommand(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/certificates/{certificateId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RejectCertificate(
        Guid caregiverId,
        Guid certificateId,
        [FromBody] ReviewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RejectCertificateCommand(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId),
                    request.Reason),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/certificates/{certificateId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeCertificate(
        Guid caregiverId,
        Guid certificateId,
        [FromBody] ReviewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RevokeCertificateCommand(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}/certificates/{certificateId:guid}/file")]
    public async Task<IActionResult> DownloadCertificateFile(
        Guid caregiverId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetCertificateFileQuery(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId)),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        // FileStreamResult disposes the stream after sending.
        return File(
            result.Value.Content,
            result.Value.ContentType,
            result.Value.FileName);
    }

    [HttpGet]
    [ProducesResponseType(
       typeof(PagedCaregiverList),
       StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCaregivers(
       [FromQuery] int page = 1,
       [FromQuery] int pageSize = 10,
       [FromQuery] CaregiverStatus? status = null,
       [FromQuery] CaregiverType? type = null,
       CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCaregiversAdminQuery(
            page,
            pageSize,
            status,
            type),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}")]
    [ProducesResponseType(
       typeof(CaregiverProfileResponse),
       StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCaregiverDetail(
       Guid caregiverId,
       CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetCaregiverAdminDetailQuery(
                    new CaregiverId(caregiverId)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ApproveCaregiverCommand(
                    new CaregiverId(caregiverId),
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(
        Guid caregiverId,
        [FromBody] ReviewCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RejectCaregiverApplicationCommand(
                    new CaregiverId(caregiverId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/request-correction")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestCorrection(
        Guid caregiverId,
        [FromBody] ReviewCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RequestCaregiverCorrectionCommand(
                    new CaregiverId(caregiverId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(
        Guid caregiverId,
        [FromBody] ReviewCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new SuspendCaregiverCommand(
                    new CaregiverId(caregiverId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ReactivateCaregiverCommand(
                    new CaregiverId(caregiverId),
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }
}