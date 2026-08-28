using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public static class LookupsErrors
{
    public static readonly Error NotFound =
        new(
            "Caregivers.Lookups.NotFound",
            "Lookup was not found.");

    public static readonly Error NameAlreadyInUse =
        new(
            "Caregivers.Lookups.NameAlreadyInUse",
            "A lookup with this name already exists.");

    public static readonly Error ParentNotFound =
        new(
            "Caregivers.Lookups.ParentNotFound",
            "The referenced parent lookup was not found.");

    public static readonly Error LanguageCodeInUse =
        new(
            "Caregivers.Lookups.LanguageCodeInUse",
            "A language with this code already exists.");
}