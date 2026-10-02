using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.API.Controllers;

public sealed record CareHomeProfileDraftRequest(
    string? ArabicName,
    string? EnglishName,
    string? ArabicDescription,
    string? EnglishDescription,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? Governorate,
    string? City,
    string? Area,
    string? Address,
    string? ArabicAdmissionConditions,
    string? EnglishAdmissionConditions,
    IReadOnlyList<BilingualCareHomeItem>? Amenities,
    IReadOnlyList<BilingualCareHomeItem>? MedicalServices)
{
    public CareHomeProfileDraft ToDomainDraft() => new(
        ArabicName ?? string.Empty,
        EnglishName ?? string.Empty,
        ArabicDescription ?? string.Empty,
        EnglishDescription ?? string.Empty,
        ContactName ?? string.Empty,
        ContactPhone ?? string.Empty,
        ContactEmail,
        Governorate ?? string.Empty,
        City ?? string.Empty,
        Area ?? string.Empty,
        Address ?? string.Empty,
        ArabicAdmissionConditions ?? string.Empty,
        EnglishAdmissionConditions ?? string.Empty,
        Amenities ?? [],
        MedicalServices ?? []);
}

public sealed record SaveCareHomeProfileRequest(int ExpectedVersion, CareHomeProfileDraftRequest Draft);
public sealed record SubmitCareHomeRequest(int ExpectedVersion);

[Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
[Route("api/v1/care-homes/facilities")]
public sealed class CareHomeFacilitiesController(ISender sender) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CareHomeOwnerProfile), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        var result = await sender.Send(new CreateCareHomeFacilityCommand(actorUserId), cancellationToken);
        if (result.IsFailure)
            return ToActionResult(result);
        return CreatedAtAction(nameof(GetMine), null, result.Value);
    }

    [HttpGet("mine")]
    [ProducesResponseType(typeof(CareHomeOwnerProfile), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        return ToActionResult(await sender.Send(new GetMyCareHomeFacilityQuery(actorUserId), cancellationToken));
    }

    [HttpPut("mine/profile")]
    [ProducesResponseType(typeof(CareHomeOwnerProfile), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveProfile(
        [FromBody] SaveCareHomeProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        return ToActionResult(await sender.Send(
            new SaveMyCareHomeProfileCommand(actorUserId, request.ExpectedVersion, request.Draft.ToDomainDraft()),
            cancellationToken));
    }

    [HttpPost("mine/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10_485_760 + 102_400)]
    [ProducesResponseType(typeof(CareHomeDocumentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadDocument(
        [FromForm] int expectedVersion,
        [FromForm] CareHomeDocumentType type,
        [FromForm] DateOnly? expiryDate,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        if (file is null)
            return BadRequest();

        await using MemoryStream content = new();
        await file.CopyToAsync(content, cancellationToken);
        content.Position = 0;
        return ToActionResult(await sender.Send(new UploadCareHomeDocumentCommand(
            actorUserId, expectedVersion, type, expiryDate, file.ContentType.ToLowerInvariant(),
            file.Length, content), cancellationToken));
    }

    [HttpPost("mine/submit")]
    [ProducesResponseType(typeof(CareHomeOwnerProfile), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitCareHomeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        return ToActionResult(await sender.Send(
            new SubmitMyCareHomeApplicationCommand(actorUserId, request.ExpectedVersion), cancellationToken));
    }
}
