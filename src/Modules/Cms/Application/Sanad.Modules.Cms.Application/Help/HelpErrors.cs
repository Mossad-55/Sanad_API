using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Help;

public static class HelpErrors
{
    public static readonly Error FaqNotFound =
        new(
            "Cms.Help.FaqNotFound",
            "FAQ entry was not found.");

    public static readonly Error SupportContactNotFound =
        new(
            "Cms.Help.SupportContactNotFound",
            "The global support contact has not been configured yet.");
}
