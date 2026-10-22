using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.API.Controllers;

/// <summary>
/// One deadline tier of the structured cancellation terms a care-home owner
/// publishes on their facility profile (clause 6, decision #9). Percentages
/// are whole numbers (0–100) of the paid total; deadlines are whole hours
/// (0–8760) before the scheduled stay start. Validation and normalization
/// (ordering, duplicate deadlines, tier-count cap) happen in the domain value
/// object; a draft with <c>null</c> cancellation terms publishes no structured
/// terms and clears any previously published ones (replacement semantics).
/// </summary>
public sealed record CareHomeCancellationTierRequest(int RefundPercentage, int DeadlineHoursBeforeStart);

/// <summary>Wire shape of the profile's structured cancellation terms, mirroring the owner profile response.</summary>
public sealed record CareHomeCancellationTermsRequest(IReadOnlyList<CareHomeCancellationTierRequest> Tiers);

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
    IReadOnlyList<BilingualCareHomeItem>? MedicalServices,
    CareHomeCancellationTermsRequest? CancellationTerms = null)
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
        MedicalServices ?? [],
        ToCancellationTerms(CancellationTerms));

    private static CareHomeCancellationTerms? ToCancellationTerms(CareHomeCancellationTermsRequest? request)
    {
        if (request is null)
            return null;
        if (request.Tiers is null)
            throw new DomainException("The cancellation terms must include a tiers list.");
        if (request.Tiers.Any(tier => tier is null))
            throw new DomainException("Cancellation terms tiers cannot contain null entries.");
        return CareHomeCancellationTerms.From([.. request.Tiers.Select(tier =>
            new CareHomeCancellationTermTier(tier.RefundPercentage, tier.DeadlineHoursBeforeStart))]);
    }
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

        // The structured cancellation terms are a self-validating domain value
        // object, so draft construction is the first point where malformed
        // tiers (bad percentage, deadline range, repeats, tier count) surface.
        // Translate that here to the same InvalidProfile failure the profile
        // handler returns for every other invalid draft field, instead of an
        // unhandled 500.
        CareHomeProfileDraft draft;
        try
        {
            draft = request.Draft.ToDomainDraft();
        }
        catch (DomainException exception)
        {
            return ToActionResult(Result<CareHomeOwnerProfile>.Failure(
                new Error("CareHomes.Facility.InvalidProfile", exception.Message)));
        }

        return ToActionResult(await sender.Send(
            new SaveMyCareHomeProfileCommand(actorUserId, request.ExpectedVersion, draft), cancellationToken));
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
