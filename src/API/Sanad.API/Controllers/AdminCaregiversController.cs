using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;

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
}