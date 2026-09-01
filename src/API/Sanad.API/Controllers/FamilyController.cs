using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Invitations;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family")]
public sealed class FamilyController :
    ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public FamilyController(
        ISender sender,
        IFileStorage fileStorage)
    {
        _sender = sender;
        _fileStorage = fileStorage;
    }

    // ------------------------------ Family ------------------------------

    [HttpPost]
    [ProducesResponseType(
        typeof(FamilyResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> BootstrapFamily(
        [FromBody] BootstrapFamilyRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BootstrapFamilyCommand(
                    userId,
                    request?.Name),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(FamilyResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFamily(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetMyFamilyQuery(userId),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("name")]
    [ProducesResponseType(
        typeof(FamilyResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> RenameFamily(
        [FromBody] RenameFamilyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new RenameFamilyCommand(
                    userId,
                    request.Name),
                cancellationToken);

        return ToActionResult(result);
    }

    // ---------------------------- Dependents ----------------------------

    [HttpPost("dependents")]
    [RequestSizeLimit(5_242_880)] // 5 MB, matches private storage limit
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(DependentResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> AddDependent(
        [FromForm] AddDependentRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        string? photoKey = null;

        if (photo is not null)
        {
            Result<StoredFile> upload =
                await SavePrivatePhotoAsync(
                    photo,
                    cancellationToken);

            if (upload.IsFailure)
            {
                return ToActionResult(upload);
            }

            photoKey = upload.Value.Key;
        }

        var result =
            await _sender.Send(
                new AddDependentCommand(
                    userId,
                    request.ArabicFullName,
                    request.EnglishFullName,
                    request.PhoneNumber,
                    request.Gender,
                    request.DateOfBirth,
                    photoKey,
                    request.DetailedAddress,
                    request.HealthNotes,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    DateTime.UtcNow),
                cancellationToken);

        if (result.IsFailure && photoKey is not null)
        {
            await _fileStorage.DeleteAsync(
                photoKey,
                cancellationToken);
        }

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet("dependents")]
    [ProducesResponseType(
        typeof(IReadOnlyList<DependentResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDependents(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new ListDependentsQuery(userId),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("dependents/{dependentId:guid}")]
    [ProducesResponseType(
        typeof(DependentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDependent(
        Guid dependentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetDependentQuery(
                    userId,
                    new ElderlyId(dependentId)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("dependents/{dependentId:guid}")]
    [ProducesResponseType(
        typeof(DependentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDependent(
        Guid dependentId,
        [FromBody] UpdateDependentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateDependentCommand(
                    userId,
                    new ElderlyId(dependentId),
                    request.ArabicFullName,
                    request.EnglishFullName,
                    request.Gender,
                    request.DateOfBirth,
                    request.DetailedAddress,
                    request.HealthNotes,
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("dependents/{dependentId:guid}")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveDependent(
        Guid dependentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new RemoveDependentCommand(
                    userId,
                    new ElderlyId(dependentId)),
                cancellationToken);

        return ToActionResult(result);
    }

    // ------------------------------ Photos ------------------------------

    [HttpPut("dependents/{dependentId:guid}/photo")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(DependentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDependentPhoto(
        Guid dependentId,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        Result<StoredFile> upload =
            await SavePrivatePhotoAsync(
                photo,
                cancellationToken);

        if (upload.IsFailure)
        {
            return ToActionResult(upload);
        }

        string photoKey = upload.Value.Key;

        var result =
            await _sender.Send(
                new SetDependentPhotoCommand(
                    userId,
                    new ElderlyId(dependentId),
                    photoKey),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                photoKey,
                cancellationToken);

            return ToActionResult(result);
        }

        return ToActionResult(result);
    }

    [HttpGet("dependents/{dependentId:guid}/photo")]
    public async Task<IActionResult> GetDependentPhoto(
        Guid dependentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetDependentPhotoQuery(
                    userId,
                    new ElderlyId(dependentId)),
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

    private async Task<Result<StoredFile>> SavePrivatePhotoAsync(
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (photo is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        await using Stream stream =
            photo.OpenReadStream();

        return await _fileStorage.SavePrivateAsync(
            stream,
            photo.ContentType,
            photo.Length,
            folder: DependentPhotoStorage.Folder,
            cancellationToken);
    }

    // --------------------------- Invitations ---------------------------

    [HttpPost("invitations")]
    [ProducesResponseType(
        typeof(FamilyInvitationResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateInvitation(
        [FromBody] CreateFamilyInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new CreateFamilyInvitationCommand(
                    userId,
                    request.Email,
                    request.Role,
                    request.RelationshipType,
                    DateTime.UtcNow),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet("invitations")]
    [ProducesResponseType(
        typeof(IReadOnlyList<FamilyInvitationResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyInvitations(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new ListMyFamilyInvitationsQuery(
                    userId,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("invitations/accept")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptFamilyInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new AcceptFamilyInvitationCommand(
                    userId,
                    request.Token,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("invitations/decline")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeclineInvitation(
        [FromBody] DeclineFamilyInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new DeclineFamilyInvitationCommand(
                    userId,
                    request.Token,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("invitations/{invitationId:guid}")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeInvitation(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new RevokeFamilyInvitationCommand(
                    userId,
                    new FamilyInvitationId(invitationId),
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }
}