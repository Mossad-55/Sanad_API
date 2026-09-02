using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Assessments;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin/assessments")]
public sealed class AdminAssessmentsController :
    ApiControllerBase
{
    private const string TierImageFolder = "assessment-tiers";
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public AdminAssessmentsController(
        ISender sender,
        IFileStorage fileStorage)
    {
        _sender = sender;
        _fileStorage = fileStorage;
    }

    // ---------------------------- Questions -----------------------------

    [HttpPost("questions")]
    [ProducesResponseType(typeof(AdminAssessmentQuestionResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateQuestion(
        [FromBody] CreateAssessmentQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateAssessmentQuestionCommand(
                request.Order,
                request.ArabicText,
                request.EnglishText,
                request.IsRequired,
                request.IsActive,
                request.Options),
            cancellationToken);

        return result.IsFailure
            ? ToActionResult(result)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("questions")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminAssessmentQuestionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListQuestions(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListAdminAssessmentQuestionsQuery(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("questions/{id:guid}")]
    [ProducesResponseType(typeof(AdminAssessmentQuestionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuestion(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetAdminAssessmentQuestionQuery(new AssessmentQuestionId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("questions/{id:guid}")]
    [ProducesResponseType(typeof(AdminAssessmentQuestionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateQuestion(
        Guid id,
        [FromBody] UpdateAssessmentQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdateAssessmentQuestionCommand(
                new AssessmentQuestionId(id),
                request.Order,
                request.ArabicText,
                request.EnglishText,
                request.IsRequired,
                request.Options),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("questions/{id:guid}/activate")]
    [ProducesResponseType(typeof(AdminAssessmentQuestionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateQuestion(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ActivateAssessmentQuestionCommand(new AssessmentQuestionId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("questions/{id:guid}/deactivate")]
    [ProducesResponseType(typeof(AdminAssessmentQuestionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateQuestion(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DeactivateAssessmentQuestionCommand(new AssessmentQuestionId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    // ------------------------------ Tiers -------------------------------

    [HttpPost("tiers")]
    [RequestSizeLimit(2_097_152)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AdminAssessmentTierResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTier(
        [FromForm] CreateAssessmentTierRequest request,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var upload = await SaveTierImageAsync(file, cancellationToken);
        if (upload.IsFailure)
        {
            return ToActionResult(upload);
        }

        string imageKey = upload.Value.Key;

        var result = await _sender.Send(
            new CreateAssessmentTierCommand(
                request.ScreenOrder,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicSubtitle,
                request.EnglishSubtitle,
                request.BackgroundColor,
                request.ArabicButtonText,
                request.EnglishButtonText,
                imageKey,
                request.MinScore,
                request.MaxScore,
                request.ArabicRecommendations,
                request.EnglishRecommendations,
                request.IsActive),
            cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(imageKey, cancellationToken);
            return ToActionResult(result);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("tiers")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminAssessmentTierResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTiers(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListAdminAssessmentTiersQuery(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("tiers/{id:guid}")]
    [ProducesResponseType(typeof(AdminAssessmentTierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTier(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetAdminAssessmentTierQuery(new AssessmentTierId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("tiers/{id:guid}")]
    [RequestSizeLimit(2_097_152)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AdminAssessmentTierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTier(
        Guid id,
        [FromForm] UpdateAssessmentTierRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        string? newImageKey = null;

        if (file is not null)
        {
            var upload = await SaveTierImageAsync(file, cancellationToken);
            if (upload.IsFailure)
            {
                return ToActionResult(upload);
            }

            newImageKey = upload.Value.Key;
        }

        var result = await _sender.Send(
            new UpdateAssessmentTierCommand(
                new AssessmentTierId(id),
                request.ScreenOrder,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicSubtitle,
                request.EnglishSubtitle,
                request.BackgroundColor,
                request.ArabicButtonText,
                request.EnglishButtonText,
                newImageKey,
                request.MinScore,
                request.MaxScore,
                request.ArabicRecommendations,
                request.EnglishRecommendations),
            cancellationToken);

        if (result.IsFailure && newImageKey is not null)
        {
            await _fileStorage.DeleteAsync(newImageKey, cancellationToken);
        }

        return ToActionResult(result);
    }

    [HttpPost("tiers/{id:guid}/activate")]
    [ProducesResponseType(typeof(AdminAssessmentTierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateTier(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ActivateAssessmentTierCommand(new AssessmentTierId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("tiers/{id:guid}/deactivate")]
    [ProducesResponseType(typeof(AdminAssessmentTierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateTier(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DeactivateAssessmentTierCommand(new AssessmentTierId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    // --------------------------- Submissions ----------------------------

    [HttpGet("submissions")]
    [ProducesResponseType(typeof(PagedAssessmentSubmissions), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSubmissions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] Guid? familyId = null,
        [FromQuery] Guid? tierId = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var result = await _sender.Send(
            new ListAdminAssessmentSubmissionsQuery(
                page,
                pageSize,
                familyId.HasValue ? new FamilyId(familyId.Value) : null,
                tierId.HasValue ? new AssessmentTierId(tierId.Value) : null),
            cancellationToken);

        return ToActionResult(result);
    }

    private async Task<Result<StoredFile>> SaveTierImageAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Result<StoredFile>.Failure(StorageErrors.Empty);
        }

        await using Stream stream = file.OpenReadStream();

        return await _fileStorage.SaveAsync(
            stream,
            file.ContentType,
            file.Length,
            folder: TierImageFolder,
            cancellationToken);
    }
}