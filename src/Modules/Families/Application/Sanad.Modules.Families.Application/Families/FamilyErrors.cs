using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Families;

public static class FamilyErrors
{
    public static readonly Error AlreadyExists =
        new("Families.Family.AlreadyExists",
            "A family already exists for this user.");

    public static readonly Error NotFound =
        new("Families.Family.NotFound",
            "The family was not found.");

    public static readonly Error InvalidName =
        new("Families.Family.InvalidName",
            "The family name is invalid.");
}