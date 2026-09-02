using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Assessments;

public static class AssessmentErrors
{
    public static readonly Error QuestionNotFound =
        new("Families.Assessment.QuestionNotFound",
            "Assessment question not found.");

    public static readonly Error TierNotFound =
        new("Families.Assessment.TierNotFound",
            "Assessment tier not found.");

    public static readonly Error NotFound =
        new("Families.Assessment.NotFound",
            "Care assessment submission not found.");

    public static readonly Error InvalidQuestion =
        new("Families.Assessment.InvalidQuestion",
            "The question details or options violate assessment invariants.");

    public static readonly Error InvalidTier =
        new("Families.Assessment.InvalidTier",
            "The tier details or score range violate assessment invariants.");

    public static readonly Error InvalidSubmission =
        new("Families.Assessment.InvalidSubmission",
            "The assessment submission contains unanswered required questions or invalid options.");
}