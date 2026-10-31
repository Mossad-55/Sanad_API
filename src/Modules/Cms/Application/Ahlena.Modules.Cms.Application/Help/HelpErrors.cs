using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Cms.Application.Help;

public static class HelpErrors
{
    public static readonly Error FaqNotFound =
        new(
            "Cms.Help.FaqNotFound",
            "FAQ entry was not found.");
}
