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

    public static readonly Error NotOwner =
        new("Families.Family.NotOwner",
            "Only the family owner can perform this action.");

    public static readonly Error AccessDenied =
        new("Families.Family.AccessDenied",
            "Your family role does not permit this action.");
}
