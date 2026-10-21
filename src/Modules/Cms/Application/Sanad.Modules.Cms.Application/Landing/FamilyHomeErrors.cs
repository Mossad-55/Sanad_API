using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Landing;

public static class FamilyHomeErrors
{
    public static readonly Error SectionNotFound =
        new("Cms.FamilyHome.SectionNotFound", "Family home section was not found.");

    public static readonly Error SectionInvalid =
        new("Cms.FamilyHome.SectionInvalid", "Family home section failed validation.");

    public static readonly Error InvalidKind =
        new("Cms.FamilyHome.InvalidKind", "Unknown family home section kind.");

    public static Error Incomplete(string gap) =>
        new("Cms.FamilyHome.Incomplete", $"Family home section cannot be published: {gap}");

    public static readonly Error PublishedHistoryConflict =
        new("Cms.FamilyHome.PublishedHistoryConflict", "Published family home history is retained and cannot be deleted.");

    public static readonly Error ReorderInvalid =
        new("Cms.FamilyHome.ReorderInvalid", "Reorder entries must reference known sections exactly once.");

    public static readonly Error StaleVersion =
        new("Cms.FamilyHome.StaleVersion", "The family home section version is stale.");

    public static readonly Error ImmutableHistory =
        new("Cms.FamilyHome.ImmutableHistory", "Published family home history is immutable.");
}