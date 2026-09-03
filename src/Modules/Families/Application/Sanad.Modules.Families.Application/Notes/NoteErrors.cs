using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Notes;

public static class NoteErrors
{
    public static readonly Error DependentNotFound =
        new("Families.Notes.DependentNotFound",
            "The dependent was not found in your family.");

    public static readonly Error AccessDenied =
        new("Families.Notes.AccessDenied",
            "Your role does not permit managing notes for this dependent.");

    public static readonly Error NotFound =
        new("Families.Notes.NotFound",
            "The specified care note was not found.");

    public static readonly Error InvalidNote =
        new("Families.Notes.InvalidNote",
            "The note details are invalid.");
}