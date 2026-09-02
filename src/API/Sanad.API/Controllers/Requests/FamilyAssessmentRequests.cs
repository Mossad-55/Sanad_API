namespace Sanad.API.Controllers.Requests;

public sealed record SubmitAssessmentAnswerItem(
    Guid QuestionId,
    Guid SelectedOptionId);

public sealed record SubmitAssessmentRequest(
    Guid? ElderlyId,
    IReadOnlyList<SubmitAssessmentAnswerItem> Answers);