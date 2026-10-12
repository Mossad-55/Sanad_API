using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Landing;

public static class LandingErrors
{
    public static readonly Error SectionNotFound =
        new("Cms.LandingSection.NotFound", "Landing section was not found.");

    public static readonly Error SectionInvalid =
        new("Cms.LandingSection.Invalid", "Landing section failed validation.");

    public static readonly Error InvalidType =
        new("Cms.Landing.InvalidType", "Unknown landing section type.");

    public static Error Incomplete(string gap) =>
        new("Cms.LandingSection.Incomplete", $"Landing section cannot be published: {gap}");

    public static readonly Error PublishedHistoryConflict =
        new("Cms.LandingSection.PublishedHistory", "Published landing history is retained and cannot be deleted.");

    public static readonly Error ReorderInvalid =
        new("Cms.Landing.ReorderInvalid", "Reorder entries must reference known sections exactly once.");
}
