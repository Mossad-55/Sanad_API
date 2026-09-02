using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Assessments;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/assessment")]
public sealed class FamilyAssessmentController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public FamilyAssessmentController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("questions")]
    [ProducesResponseType(
        typeof(IReadOnlyList<FamilyAssessmentQuestionResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuestions(
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetFamilyAssessmentQuestionsQuery(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("tiers")]
    [ProducesResponseType(
        typeof(IReadOnlyList<FamilyAssessmentTierResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTiers(
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetFamilyAssessmentTiersQuery(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(FamilyAssessmentResultResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var answers = (request.Answers ?? [])
            .Select(a => new AssessmentAnswerInput(
                new AssessmentQuestionId(a.QuestionId),
                new AssessmentOptionId(a.SelectedOptionId)))
            .ToList();

        var result = await _sender.Send(
            new SubmitAssessmentCommand(
                userId,
                request.ElderlyId.HasValue
                    ? new ElderlyId(request.ElderlyId.Value)
                    : null,
                answers,
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
}