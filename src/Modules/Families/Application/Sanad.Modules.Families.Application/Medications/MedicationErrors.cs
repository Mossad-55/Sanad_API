using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Medications;

public static class MedicationErrors
{
    public static readonly Error DependentNotFound =
        new("Families.Medication.DependentNotFound",
            "The dependent was not found in your family.");

    public static readonly Error AccessDenied =
        new("Families.Medication.AccessDenied",
            "Your family role does not permit managing medications.");

    public static readonly Error NotFound =
        new("Families.Medication.NotFound",
            "The medication was not found.");

    public static readonly Error DoseNotFound =
        new("Families.Medication.DoseNotFound",
            "The specified scheduled dose was not found.");

    public static readonly Error DoseAlreadyTaken =
        new("Families.Medication.DoseAlreadyTaken",
            "This dose has already been marked as taken.");

    public static readonly Error InvalidMedication =
        new("Families.Medication.InvalidMedication",
            "The medication details are invalid.");

    public static readonly Error InvalidDateRange =
        new("Families.Medication.InvalidDateRange",
            "The specified date range is invalid.");
}